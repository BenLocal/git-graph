using GitGraph.Core.Git;

namespace GitGraph.Core.Workspaces;

/// <summary>A repository found in a workspace, with every worktree that shares its .git directory.</summary>
public sealed record DiscoveredRepo(string Name, string CommonDir, string MainPath, IReadOnlyList<WorktreeInfo> Worktrees);

public static class RepoDiscovery
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "target", "build", "dist", ".venv", "venv", "__pycache__", ".idea", ".vs",
    };

    /// <summary>Finds working trees under <paramref name="root"/>, including the folder that contains it if that is a repo.</summary>
    public static IEnumerable<string> FindWorkingTrees(string root, int maxDepth = 3)
    {
        if (!Directory.Exists(root)) yield break;
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            var dotGit = Path.Combine(dir, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit))
            {
                yield return dir;
                // Nested repositories (vendored or submodules) are rare enough to skip; it keeps scans fast.
                continue;
            }
            if (depth >= maxDepth) continue;
            string[] children;
            try { children = Directory.GetDirectories(dir); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { continue; }
            foreach (var child in children.OrderByDescending(c => c, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(child);
                if (name.StartsWith('.') || SkipDirs.Contains(name)) continue;
                stack.Push((child, depth + 1));
            }
        }
    }

    /// <summary>Scans all folders and groups the working trees found by the repository they belong to.</summary>
    public static async Task<List<DiscoveredRepo>> DiscoverAsync(IEnumerable<string> folders, CancellationToken ct = default)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            var found = await Task.Run(() => FindWorkingTrees(folder).ToList(), ct);
            if (found.Count == 0 && await GitRepository.FindTopLevelAsync(folder, ct) is { } top)
                found.Add(top); // the folder lives inside a repository
            foreach (var p in found) paths.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)));
        }

        var byCommon = new Dictionary<string, string>(StringComparer.Ordinal);
        // git prints paths with symlinks resolved (/var/... becomes /private/var/... on macOS). The workspace compares
        // repository paths with the folders the user added, so map each top level git reports back to its scanned spelling.
        var scanned = new Dictionary<string, string>(StringComparer.Ordinal);
        var tasks = paths.Select(async p => (Path: p, Result: await GitRunner.RunRawAsync(
            p, ["rev-parse", "--path-format=absolute", "--git-common-dir", "--show-toplevel"], ct)));
        foreach (var (p, r) in await Task.WhenAll(tasks))
        {
            var lines = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (r.ExitCode != 0 || lines.Length < 2) continue;
            byCommon.TryAdd(Path.TrimEndingDirectorySeparator(lines[0]), p);
            scanned.TryAdd(Path.TrimEndingDirectorySeparator(lines[1]), p);
        }

        var repos = new List<DiscoveredRepo>();
        foreach (var (common, anyPath) in byCommon)
        {
            List<WorktreeInfo> wts;
            try { wts = await new GitRepository(anyPath).GetWorktreesAsync(ct); }
            catch (GitException) { wts = []; }
            wts = wts.Select(w => scanned.TryGetValue(Path.TrimEndingDirectorySeparator(w.Path), out var p) ? w with { Path = p } : w).ToList();
            var main = wts.FirstOrDefault(w => w.IsMain && !w.IsBare)?.Path ?? anyPath;
            repos.Add(new DiscoveredRepo(Path.GetFileName(main), common, main, wts));
        }
        repos.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return repos;
    }
}
