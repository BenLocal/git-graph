using System.Text;
using GitGraph.Core.Git;
using GitGraph.Core.Workspaces;

namespace GitGraph.Core.Navigation;

/// <summary>A place one folder's search runs in: the folder itself or a repository inside it.</summary>
/// <param name="Prefix">Its path relative to the folder searched, with '/' ("" for the folder itself).</param>
public sealed record SearchRoot(string Dir, bool IsGit, string Prefix);

/// <summary>
/// Searches over a folder's files for go to file and find in files. A workspace folder often holds several
/// repositories without being one, so a folder is searched through its own repository (from the folder down) and
/// every repository inside it: files from <c>git ls-files</c> (tracked and untracked, not ignored) and text from
/// <c>git grep</c>. A folder with no repository at all, or one its repository ignores, is walked and searched with
/// <c>git grep --no-index</c>. Paths are relative to the folder, with '/'.
/// </summary>
public static class WorkspaceSearch
{
    /// <summary>The most files a folder outside git lists; a walk of a huge tree would otherwise never end.</summary>
    public const int MaxWalkedFiles = 100_000;

    /// <summary>Where a search of <paramref name="folder"/> runs.</summary>
    public static async Task<List<SearchRoot>> RootsAsync(string folder, CancellationToken ct = default)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var roots = new List<SearchRoot>();
        // Exit code 0: the folder is ignored (git check-ignore); 1: it isn't; 128: not in a repository.
        if (await GitRepository.FindTopLevelAsWrittenAsync(folder, ct) != null
            && (await GitRunner.RunRawAsync(folder, ["check-ignore", "-q", "."], ct)).ExitCode == 1)
            roots.Add(new SearchRoot(folder, true, ""));
        foreach (var tree in await Task.Run(() => RepoDiscovery.FindWorkingTrees(folder).ToList(), ct))
        {
            var prefix = Path.GetRelativePath(folder, tree).Replace('\\', '/');
            if (prefix != ".") roots.Add(new SearchRoot(tree, true, prefix));
        }
        if (roots.Count == 0) roots.Add(new SearchRoot(folder, false, ""));
        return roots;
    }

    public static async Task<List<string>> ListFilesAsync(string folder, CancellationToken ct = default)
    {
        var roots = await RootsAsync(folder, ct);
        // The repositories inside are listed on their own: their folder (or submodule entry) isn't a file.
        var nested = roots.Where(r => r.Prefix.Length > 0).Select(r => r.Prefix).ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var root in roots)
        {
            IEnumerable<string> files = root.IsGit
                ? (await GitRunner.RunAsync(root.Dir, ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], ct))
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                : await Task.Run(() => Walk(root.Dir, ct), ct);
            result.AddRange(files
                // A conflicted file is listed once per stage, an untracked repository as "name/".
                .Where(f => !f.EndsWith('/'))
                .Distinct(StringComparer.Ordinal)
                .Select(f => Join(root.Prefix, f))
                .Where(f => !nested.Contains(f)));
        }
        return result;
    }

    private static string Join(string prefix, string path) => prefix.Length == 0 ? path : $"{prefix}/{path}";

    private static List<string> Walk(string root, CancellationToken ct)
    {
        var result = new List<string>();
        var pending = new Stack<string>([root]);
        while (pending.Count > 0 && result.Count < MaxWalkedFiles)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (entry is DirectoryInfo d)
                    {
                        // Linked folders could loop back on themselves.
                        if (d.Name != ".git" && !d.Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(d.FullName);
                    }
                    else result.Add(Path.GetRelativePath(root, entry.FullName).Replace('\\', '/'));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* unreadable: skipped */ }
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>
    /// Lines containing <paramref name="query"/> as plain text, the first match of each line, at most
    /// <paramref name="max"/>. Case is ignored unless the query has a capital letter, as in VS Code's smart case.
    /// </summary>
    public static async Task<List<NavLocation>> SearchTextAsync(string folder, string query, int max, CancellationToken ct = default)
    {
        var result = new List<NavLocation>();
        if (query.Length == 0) return result;
        foreach (var root in await RootsAsync(folder, ct))
        {
            if (result.Count >= max) break;
            foreach (var l in await SearchTextAsync(root, query, max - result.Count, ct))
                result.Add(l with { Path = Join(root.Prefix, l.Path) });
        }
        return result;
    }

    private static async Task<List<NavLocation>> SearchTextAsync(SearchRoot root, string query, int max, CancellationToken ct)
    {
        var args = new List<string> { "grep", "-z", "-n", "--column", "-I", "--no-color", "--fixed-strings" };
        if (!query.Any(char.IsUpper)) args.Add("-i");
        // A repository's grep doesn't go into the repositories inside it, which are searched on their own.
        args.Add(root.IsGit ? "--untracked" : "--no-index");
        args.AddRange(["-e", query]);
        // Exit code 1 means no match.
        var output = await GitRunner.RunAsync(root.Dir, args, ct, 1);

        // "<path>\0<line>\0<column>\0<text>" per line; the column is 1-based and counts bytes.
        var hits = new List<(string Path, int Line, int Column, string Text)>();
        foreach (var record in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (hits.Count == max) break;
            var parts = record.Split('\0', 4);
            if (parts.Length < 4 || !int.TryParse(parts[1], out var line) || !int.TryParse(parts[2], out var column)) continue;
            var text = parts[3].TrimEnd('\r');
            // git counts a byte order mark as part of the first line; the viewer drops it.
            if (line == 1 && text.StartsWith('﻿'))
            {
                text = text[1..];
                column -= Encoding.UTF8.GetByteCount("﻿");
            }
            var bytes = Encoding.UTF8.GetBytes(text);
            var charColumn = Encoding.UTF8.GetCharCount(bytes, 0, Math.Clamp(column - 1, 0, bytes.Length));
            hits.Add((parts[0], line - 1, charColumn, text));
        }

        // Offsets into each file, which the viewer and the preview place the caret by.
        var result = new List<NavLocation>(hits.Count);
        foreach (var file in hits.GroupBy(h => h.Path))
        {
            ct.ThrowIfCancellationRequested();
            var starts = LineStarts(Path.Combine(root.Dir, file.Key));
            foreach (var (path, line, column, text) in file)
            {
                var start = (line < starts.Count ? starts[line] : 0) + column;
                result.Add(new NavLocation(path, new TextSpan(start, start + query.Length, line, column), text.Trim(), null));
            }
        }
        return result;
    }

    /// <summary>Where each line of a file starts, in UTF-16 code units of its text as the viewer decodes it.</summary>
    private static List<int> LineStarts(string file)
    {
        var starts = new List<int> { 0 };
        try
        {
            var text = File.ReadAllText(file);
            for (var i = 0; i < text.Length; i++)
                if (text[i] == '\n') starts.Add(i + 1);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* gone: offsets fall back to the line's start */ }
        return starts;
    }
}
