using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GitGraph.Core.Git;

namespace GitGraph.Core.Navigation;

/// <summary>How a sync went: files parsed, files pointed at content already in the index, files dropped.</summary>
public sealed record SyncResult(int Parsed, int Linked, int Removed, int Unchanged);

/// <summary>
/// Brings a work tree's part of a <see cref="SymbolIndex"/> up to date. In a git work tree, tracked files are
/// compared by blob hash (<c>git ls-files -s</c>) and modified or untracked ones (<c>git status</c>) by mtime and
/// size; a plain folder is walked and compared by mtime and size. Only files whose content isn't in the index yet
/// are parsed. Ignored files and files of unsupported languages are skipped.
/// </summary>
public static class IndexSync
{
    /// <summary>Larger files are generated or vendored more often than written; the viewer doesn't show them either.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    private const int BatchSize = 200;

    // git's own heuristic: a NUL byte in the first 8000 bytes makes a file binary.
    private const int BinarySniffLength = 8000;

    // One sync per index file at a time; a second caller waits, then finds little left to do.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    /// <summary>Syncs <paramref name="root"/> (a git work tree's top level, or a plain folder).</summary>
    public static async Task<SyncResult> SyncAsync(SymbolIndex index, string indexFile, string root, bool isGit,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var gate = Locks.GetOrAdd(indexFile, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var wanted = isGit ? await GitFilesAsync(root, index.Files(root), ct) : WalkFiles(root, index.Files(root), ct);
            return await ApplyAsync(index, root, wanted, progress, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A file the work tree has now: its content hash, and the mtime and size it was hashed at.</summary>
    private sealed record Wanted(string Path, string Blob, long Mtime, long Size);

    private static async Task<Dictionary<string, Wanted>> GitFilesAsync(string root, Dictionary<string, SymbolIndex.FileRow> known, CancellationToken ct)
    {
        var lsFiles = GitRunner.RunAsync(root, ["ls-files", "-s", "-z"], ct);
        var status = GitRunner.RunAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--no-renames"], ct);
        var wanted = new Dictionary<string, Wanted>(StringComparer.Ordinal);

        // "<mode> <hash> <stage>\t<path>"; submodules (160000) and conflict stages are skipped.
        foreach (var entry in (await lsFiles).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0) continue;
            var meta = entry[..tab].Split(' ');
            var path = entry[(tab + 1)..];
            if (meta.Length < 3 || meta[0] == "160000" || meta[2] != "0" || NavLanguage.ForPath(path) == null) continue;
            // The size limit applies to committed files too; a deleted one is left to the status below.
            if (new FileInfo(Path.Combine(root, path)) is { Exists: true, Length: > MaxFileBytes }) continue;
            wanted[path] = new Wanted(path, meta[1], 0, 0);
        }

        // "XY <path>": the work tree differs from the index where Y isn't a space, and "??" is untracked.
        foreach (var entry in (await status).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Length < 4) continue;
            var (y, path) = (entry[1], entry[3..]);
            if (y == ' ' || NavLanguage.ForPath(path) == null) continue;
            if (y == 'D')
            {
                wanted.Remove(path);
                continue;
            }
            if (OnDisk(root, path, known) is { } w) wanted[path] = w;
            else wanted.Remove(path);
        }
        return wanted;
    }

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "target" };

    private static Dictionary<string, Wanted> WalkFiles(string root, Dictionary<string, SymbolIndex.FileRow> known, CancellationToken ct)
    {
        var wanted = new Dictionary<string, Wanted>(StringComparer.Ordinal);
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            IEnumerable<string> dirs, files;
            try
            {
                dirs = Directory.EnumerateDirectories(dir).ToList();
                files = Directory.EnumerateFiles(dir).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var d in dirs)
            {
                var name = Path.GetFileName(d);
                if (!name.StartsWith('.') && !SkippedFolders.Contains(name)) pending.Push(d);
            }
            foreach (var f in files)
            {
                var path = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (NavLanguage.ForPath(path) != null && OnDisk(root, path, known) is { } w) wanted[path] = w;
            }
        }
        return wanted;
    }

    /// <summary>A file on disk: the hash the index already has if mtime and size still match, or a new hash.</summary>
    private static Wanted? OnDisk(string root, string path, Dictionary<string, SymbolIndex.FileRow> known)
    {
        var info = new FileInfo(Path.Combine(root, path));
        if (!info.Exists || info.Length > MaxFileBytes) return null;
        var mtime = info.LastWriteTimeUtc.Ticks;
        if (known.TryGetValue(path, out var row) && row.Mtime == mtime && row.Size == info.Length)
            return new Wanted(path, row.Blob, mtime, info.Length);
        try
        {
            return new Wanted(path, BlobHash(File.ReadAllBytes(info.FullName)), mtime, info.Length);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>git's blob hash of some content, so unmodified files hash the same as in <c>git ls-files -s</c>.</summary>
    public static string BlobHash(byte[] content)
    {
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha.AppendData(header);
        sha.AppendData(content);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static async Task<SyncResult> ApplyAsync(SymbolIndex index, string root, Dictionary<string, Wanted> wanted,
        IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        var known = index.Files(root);
        var blobs = index.Blobs();
        var changed = wanted.Values.Where(w => !known.TryGetValue(w.Path, out var k) || k.Blob != w.Blob || k.Mtime != w.Mtime).ToList();
        var removed = known.Keys.Where(p => !wanted.ContainsKey(p)).ToList();
        var unchanged = wanted.Count - changed.Count;

        // Content the index has (another worktree, an earlier branch) is only linked; the rest is parsed, once per blob.
        var linked = changed.Where(w => blobs.Contains(w.Blob)).ToList();
        var toParse = changed.Where(w => !blobs.Contains(w.Blob)).ToList();
        var parsedCount = toParse.Select(w => w.Blob).Distinct().Count();

        using (var batch = index.BeginBatch())
        {
            foreach (var w in linked) batch.SetFile(root, w.Path, w.Blob, w.Mtime, w.Size);
            foreach (var p in removed) batch.RemoveFile(root, p);
            batch.Commit();
        }

        var done = 0;
        progress?.Report((0, toParse.Count));
        foreach (var chunk in toParse.Chunk(BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            var results = await Task.Run(() => ParseAll(root, chunk, ct), ct);
            using var batch = index.BeginBatch();
            foreach (var (w, language, symbols) in results)
            {
                batch.AddBlob(w.Blob, language, symbols ?? FileSymbols.Empty);
                batch.SetFile(root, w.Path, w.Blob, w.Mtime, w.Size);
            }
            batch.Commit();
            done += chunk.Length;
            progress?.Report((done, toParse.Count));
        }
        // Content that no file points at any more (the old version of a changed file).
        using (var batch = index.BeginBatch())
        {
            batch.RemoveUnusedBlobs();
            batch.Commit();
        }
        return new SyncResult(parsedCount, linked.Count, removed.Count, unchanged);
    }

    /// <summary>Parses files in parallel, one parser per worker (tree-sitter parsers aren't thread-safe).</summary>
    private static List<(Wanted File, NavLanguage Language, FileSymbols? Symbols)> ParseAll(string root, Wanted[] files, CancellationToken ct)
    {
        var results = new ConcurrentBag<(Wanted, NavLanguage, FileSymbols?)>();
        Parallel.ForEach(files, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
            () => new SourceParser(),
            (w, _, parser) =>
            {
                var language = NavLanguage.ForPath(w.Path)!;
                FileSymbols? symbols = null;
                try
                {
                    var bytes = File.ReadAllBytes(Path.Combine(root, w.Path));
                    // Binary files with a source extension (.ts videos) are stored empty instead of parsed.
                    if (!bytes.AsSpan(0, Math.Min(bytes.Length, BinarySniffLength)).Contains((byte)0))
                    {
                        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                        using var src = parser.Parse(language, reader.ReadToEnd());
                        symbols = FileSymbols.From(src);
                    }
                }
                catch (IOException)
                {
                    // Gone or locked since it was listed; stored empty, and its next change re-parses it.
                }
                results.Add((w, language, symbols));
                return parser;
            },
            parser => parser.Dispose());
        return results.ToList();
    }
}
