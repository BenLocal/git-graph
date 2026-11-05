using System.Text.RegularExpressions;
using GitGraph.Core.Git;

namespace GitGraph.Core.Navigation;

/// <summary>A place to jump to: a file relative to the root, a span in it, its line's text and a short description.</summary>
public sealed record NavLocation(string Path, TextSpan Span, string LineText, string? Detail);

/// <summary>The name that was looked up (null when the offset isn't on a name) and where it leads.</summary>
public sealed record NavResult(string? Name, IReadOnlyList<NavLocation> Locations)
{
    public static readonly NavResult None = new(null, []);
}

/// <summary>
/// Go to definition, find references and go to implementation for one work tree (or plain folder). Locals are
/// resolved by scope in the current file; everything else is found by name through the <see cref="SymbolIndex"/>,
/// or with <c>git grep</c> while there is no index, then parsed to tell definitions from uses. Results are
/// syntax-based: an unrelated symbol with the same name can show up, ranked after closer ones.
/// </summary>
/// <param name="index">A connection of its own: a SQLite connection must not be shared with a running sync.</param>
public sealed class CodeNavigator(string root, bool isGit, SymbolIndex? index) : IDisposable
{
    /// <summary>The most files a lookup without the index parses.</summary>
    public const int MaxFallbackFiles = 500;

    private readonly SourceParser _parser = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    // The file in the viewer, parsed once for all the lookups and caret moves in it.
    private (string Path, string Text, ParsedSource? Source)? _current;

    public string Root { get; } = root;

    /// <summary>Waits for a lookup in progress, which uses the parser.</summary>
    public void Dispose()
    {
        _gate.Wait();
        _current?.Source?.Dispose();
        _parser.Dispose();
        _gate.Dispose();
    }

    public Task<NavResult> DefinitionsAsync(string path, string text, int offset, CancellationToken ct = default) =>
        RunAsync(() => Definitions(path, text, offset, ct), ct);

    public Task<NavResult> ReferencesAsync(string path, string text, int offset, CancellationToken ct = default) =>
        RunAsync(() => References(path, text, offset, ct), ct);

    public Task<NavResult> ImplementationsAsync(string path, string text, int offset, CancellationToken ct = default) =>
        RunAsync(() => Implementations(path, text, offset, ct), ct);

    /// <summary>
    /// Where the name at an offset occurs in its file, for highlighting: a local's uses, or every use of any other
    /// name that isn't a local of the same name. Empty off a name or in a file no grammar covers.
    /// </summary>
    public Task<IReadOnlyList<TextSpan>> OccurrencesAsync(string path, string text, int offset, CancellationToken ct = default) =>
        RunAsync<IReadOnlyList<TextSpan>>(() =>
        {
            if (Current(path, text) is not { } src || src.IdentifierAt(offset) is not { } id) return [];
            if (src.ResolveLocal(offset) is { } local) return local.Uses;
            return Uses(path, src, id.Name).Select(l => l.Span).ToList();
        }, ct);

    /// <summary>
    /// The name a Ctrl+click at an offset would look up, for underlining it: an identifier (none in comments or
    /// strings), or the word there in a file no grammar covers.
    /// </summary>
    public Task<TextSpan?> NameAtAsync(string path, string text, int offset, CancellationToken ct = default) =>
        RunAsync(() =>
        {
            if (Current(path, text) is { } src) return src.IdentifierAt(offset)?.Span;
            return WordSpanAt(text, offset) is var (start, end) ? SpanAt(text, start, end - start) : (TextSpan?)null;
        }, ct);

    private ParsedSource? Current(string path, string text)
    {
        if (_current is { } c && c.Path == path && ReferenceEquals(c.Text, text)) return c.Source;
        _current?.Source?.Dispose();
        var src = _parser.Parse(path, text);
        _current = (path, text, src);
        return src;
    }

    // The parser isn't thread-safe, and parsing is CPU work: one lookup at a time, off the caller's thread.
    private async Task<T> RunAsync<T>(Func<T> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await Task.Run(work, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private NavResult Definitions(string path, string text, int offset, CancellationToken ct)
    {
        var src = Current(path, text);
        if (src == null) return Words(path, text, offset, ct);
        if (src.IdentifierAt(offset) is not { } id) return NavResult.None;
        if (src.ResolveLocal(offset) is { } local)
            return new NavResult(id.Name, [Location(path, text, local.Declaration, "local")]);

        var found = new List<(string Path, string? Text, SymbolDef Symbol)>();
        found.AddRange(src.Definitions().Where(d => d.Name == id.Name).Select(d => (path, (string?)text, d)));
        if (index != null)
        {
            found.AddRange(index.FindDefinitions(Root, id.Name).Where(d => d.Path != path).Select(d => (d.Path, (string?)null, d.Symbol)));
        }
        else
        {
            foreach (var (file, other) in ParseOthers(path, id.Name, ct))
            {
                using (other) found.AddRange(other.Definitions().Where(d => d.Name == id.Name).Select(d => (file, (string?)other.Text, d)));
            }
        }
        // A type's constructors share its name; the type is where its name leads.
        if (found.Any(f => f.Symbol.Kind is SymbolKind.Class or SymbolKind.Struct))
            found.RemoveAll(f => f.Symbol.Kind == SymbolKind.Constructor);
        return new NavResult(id.Name, Rank(path, found.Select(f => Location(f.Path, f.Text, f.Symbol.Span, Describe(f.Symbol)))));
    }

    private NavResult References(string path, string text, int offset, CancellationToken ct)
    {
        var src = Current(path, text);
        if (src == null) return Words(path, text, offset, ct);
        if (src.IdentifierAt(offset) is not { } id) return NavResult.None;
        if (src.ResolveLocal(offset) is { } local)
            return new NavResult(id.Name, local.Uses.Select(u => Location(path, text, u, null)).ToList());

        var found = Uses(path, src, id.Name).ToList();
        var files = index != null
            ? index.FilesMentioning(Root, id.Name).Where(f => f != path).Select(f => (f, Parse(f)))
            : ParseOthers(path, id.Name, ct).Select(x => (x.Path, (ParsedSource?)x.Source));
        foreach (var (file, other) in files)
        {
            ct.ThrowIfCancellationRequested();
            if (other == null) continue;
            using (other) found.AddRange(Uses(file, other, id.Name));
        }
        return new NavResult(id.Name, Rank(path, found));
    }

    /// <summary>Uses of a name in a file that aren't a local of the same name.</summary>
    private IEnumerable<NavLocation> Uses(string path, ParsedSource src, string name) =>
        src.Identifiers()
            .Where(i => i.Name == name && (i.IsMember || src.ResolveLocal(i.Span.Start) == null))
            .Select(i => Location(path, src.Text, i.Span, null));

    private NavResult Implementations(string path, string text, int offset, CancellationToken ct)
    {
        var src = Current(path, text);
        if (src?.IdentifierAt(offset) is not { } id) return NavResult.None;
        var found = new List<NavLocation>();

        // Types that inherit or implement the name, directly or through other types (breadth-first, each type once).
        var subtypes = new List<(string Path, SuperRef Super)>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { id.Name };
        var queue = new Queue<string>([id.Name]);
        while (queue.Count > 0 && subtypes.Count < MaxFallbackFiles)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var s in Subtypes(path, src, queue.Dequeue(), ct))
            {
                subtypes.Add(s);
                if (seen.Add(s.Super.TypeName)) queue.Enqueue(s.Super.TypeName);
            }
        }
        foreach (var (file, super) in subtypes)
            found.Add(Location(file, file == path ? text : null, super.Span, $"{super.TypeName} : {super.SuperName}"));

        // On a member of a type (IShape.Area): the members of that name in its subtypes.
        if (found.Count == 0 && src.Definitions().FirstOrDefault(d => d.Span.Start == id.Span.Start) is { Container: { } container })
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            queue.Enqueue(container);
            while (queue.Count > 0)
                foreach (var s in Subtypes(path, src, queue.Dequeue(), ct))
                    if (types.Add(s.Super.TypeName)) queue.Enqueue(s.Super.TypeName);
            var defs = index != null
                ? index.FindDefinitions(Root, id.Name).Where(d => d.Path != path).Select(d => (d.Path, (string?)null, d.Symbol))
                : ParseOthers(path, id.Name, ct).SelectMany(x =>
                {
                    using (x.Source) return x.Source.Definitions().Select(d => (x.Path, (string?)x.Source.Text, d)).ToList();
                });
            foreach (var d in src.Definitions().Where(d => d.Name == id.Name && d.Container is { } c && types.Contains(c)))
                found.Add(Location(path, text, d.Span, Describe(d)));
            foreach (var (file, fileText, d) in defs)
                if (d.Name == id.Name && d.Container is { } c && types.Contains(c)) found.Add(Location(file, fileText, d.Span, Describe(d)));
        }
        return new NavResult(id.Name, Rank(path, found));
    }

    private IEnumerable<(string Path, SuperRef Super)> Subtypes(string path, ParsedSource src, string name, CancellationToken ct)
    {
        foreach (var s in src.Supers().Where(s => s.SuperName == name)) yield return (path, s);
        if (index != null)
        {
            foreach (var s in index.FindSubtypes(Root, name).Where(s => s.Path != path)) yield return (s.Path, s.Super);
            yield break;
        }
        foreach (var (file, other) in ParseOthers(path, name, ct))
        {
            using (other)
                foreach (var s in other.Supers().Where(s => s.SuperName == name).ToList()) yield return (file, s);
        }
    }

    /// <summary>For files no grammar covers: every whole-word occurrence of the word at the offset, by text.</summary>
    private NavResult Words(string path, string text, int offset, CancellationToken ct)
    {
        if (WordAt(text, offset) is not { } word) return NavResult.None;
        var found = new List<NavLocation>();
        foreach (var file in new[] { path }.Concat(GrepFiles(word, ct).Where(f => f != path)))
        {
            ct.ThrowIfCancellationRequested();
            var content = file == path ? text : Read(file);
            if (content == null) continue;
            foreach (Match m in Regex.Matches(content, $@"(?<![\w$]){Regex.Escape(word)}(?![\w$])"))
                found.Add(Location(file, content, SpanAt(content, m.Index, m.Length), null));
        }
        return new NavResult(word, Rank(path, found));
    }

    private static string? WordAt(string text, int offset) => WordSpanAt(text, offset) is var (start, end) ? text[start..end] : null;

    private static (int Start, int End)? WordSpanAt(string text, int offset)
    {
        if (offset < 0 || offset > text.Length) return null;
        int start = offset, end = offset;
        while (start > 0 && IsWord(text[start - 1])) start--;
        while (end < text.Length && IsWord(text[end])) end++;
        return end > start ? (start, end) : null;
        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>Other files of a supported language mentioning a name, parsed; callers dispose each.</summary>
    private IEnumerable<(string Path, ParsedSource Source)> ParseOthers(string path, string name, CancellationToken ct)
    {
        foreach (var file in GrepFiles(name, ct).Where(f => f != path && NavLanguage.ForPath(f) != null))
        {
            ct.ThrowIfCancellationRequested();
            if (Parse(file) is { } src) yield return (file, src);
        }
    }

    private List<string> GrepFiles(string name, CancellationToken ct)
    {
        if (!isGit) return [];
        // Tracked and untracked files, not ignored ones; exit code 1 means no match.
        var output = GitRunner.RunAsync(Root, ["grep", "-l", "-z", "-I", "-w", "-F", "--untracked", "-e", name], ct, 1)
            .GetAwaiter().GetResult();
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Take(MaxFallbackFiles).ToList();
    }

    private ParsedSource? Parse(string file) => Read(file) is { } text ? _parser.Parse(file, text) : null;

    private string? Read(string file)
    {
        try
        {
            var full = Path.Combine(Root, file);
            return new FileInfo(full).Length > IndexSync.MaxFileBytes ? null : File.ReadAllText(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private NavLocation Location(string file, string? text, TextSpan span, string? detail)
    {
        text ??= Read(file) ?? "";
        return new NavLocation(file, span, LineAt(text, span.Start), detail);
    }

    private static string LineAt(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        var end = text.IndexOf('\n', offset);
        return text[start..(end < 0 ? text.Length : end)].TrimEnd('\r').Trim();
    }

    private static TextSpan SpanAt(string text, int start, int length)
    {
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < start; i++)
        {
            if (text[i] != '\n') continue;
            line++;
            lineStart = i + 1;
        }
        return new TextSpan(start, start + length, line, start - lineStart);
    }

    private static string Describe(SymbolDef d) =>
        d.Container is { } c ? $"{d.Kind.ToString().ToLowerInvariant()} in {c}" : d.Kind.ToString().ToLowerInvariant();

    /// <summary>The current file first, then its folder, then files of the same language, then the rest; each by path and position.</summary>
    private static List<NavLocation> Rank(string path, IEnumerable<NavLocation> found)
    {
        var dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
        var language = NavLanguage.ForPath(path);
        return found
            .DistinctBy(l => (l.Path, l.Span.Start))
            .OrderBy(l => l.Path == path ? 0
                : (Path.GetDirectoryName(l.Path)?.Replace('\\', '/') ?? "") == dir ? 1
                : NavLanguage.ForPath(l.Path) == language ? 2 : 3)
            .ThenBy(l => l.Path, StringComparer.Ordinal)
            .ThenBy(l => l.Span.Start)
            .ToList();
    }
}
