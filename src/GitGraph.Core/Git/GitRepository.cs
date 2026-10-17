namespace GitGraph.Core.Git;

/// <summary>Queries and commands against one working tree (a repository's main checkout or one of its worktrees).</summary>
public sealed class GitRepository(string path)
{
    /// <summary>Hash of git's empty tree, used as the "parent" of root commits.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    public string Path { get; } = path;

    private Task<string> Git(CancellationToken ct, params string[] args) => GitRunner.RunAsync(Path, args, ct);

    public static async Task<string?> FindTopLevelAsync(string dir, CancellationToken ct = default)
    {
        var r = await GitRunner.RunRawAsync(dir, ["rev-parse", "--show-toplevel"], ct);
        return r.ExitCode == 0 ? r.StdOut.Trim() : null;
    }

    /// <summary>Absolute path of the shared .git directory; identical for a repository and all its worktrees.</summary>
    public async Task<string?> GetCommonDirAsync(CancellationToken ct = default)
    {
        var r = await GitRunner.RunRawAsync(Path, ["rev-parse", "--path-format=absolute", "--git-common-dir"], ct);
        return r.ExitCode == 0 ? System.IO.Path.TrimEndingDirectorySeparator(r.StdOut.Trim()) : null;
    }

    public async Task<string?> GetGitDirAsync(CancellationToken ct = default)
    {
        var r = await GitRunner.RunRawAsync(Path, ["rev-parse", "--path-format=absolute", "--git-dir"], ct);
        return r.ExitCode == 0 ? r.StdOut.Trim() : null;
    }

    public async Task<HeadInfo> GetHeadAsync(CancellationToken ct = default)
    {
        var hash = await GitRunner.RunRawAsync(Path, ["rev-parse", "--verify", "-q", "HEAD"], ct);
        var branch = await GitRunner.RunRawAsync(Path, ["symbolic-ref", "-q", "--short", "HEAD"], ct);
        return new HeadInfo(
            hash.ExitCode == 0 ? hash.StdOut.Trim() : null,
            branch.ExitCode == 0 ? branch.StdOut.Trim() : null);
    }

    /// <param name="branches">Refs to show; null or empty shows every branch and tag.</param>
    public async Task<List<CommitInfo>> GetCommitsAsync(int maxCount, IReadOnlyList<string>? branches, bool includeRemotes, CancellationToken ct = default)
    {
        var args = new List<string> { "log", "--date-order", $"--max-count={maxCount}", $"--format={GitParsers.LogFormat}" };
        if (branches is { Count: > 0 })
        {
            args.AddRange(branches);
            // HEAD keeps the current checkout visible even when the filter names other branches.
            args.Add("HEAD");
        }
        else
        {
            args.Add("--exclude=refs/stash");
            if (!includeRemotes) args.Add("--exclude=refs/remotes/*");
            args.Add("--all");
            args.Add("HEAD");
        }
        args.Add("--");
        var r = await GitRunner.RunRawAsync(Path, args, ct);
        // An empty repository has no HEAD; that is "no commits", not an error.
        if (r.ExitCode != 0)
        {
            if (r.StdErr.Contains("unknown revision", StringComparison.OrdinalIgnoreCase) ||
                r.StdErr.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase) ||
                r.StdErr.Contains("bad revision 'HEAD'", StringComparison.OrdinalIgnoreCase))
                return [];
            throw new GitException(r.StdErr.Trim(), r.ExitCode, string.Join(' ', args));
        }
        return GitParsers.ParseLog(r.StdOut);
    }

    public async Task<List<GitRef>> GetRefsAsync(CancellationToken ct = default) =>
        GitParsers.ParseRefs(await Git(ct, "for-each-ref", $"--format={GitParsers.RefFormat}", "refs/heads", "refs/remotes", "refs/tags"));

    public async Task<List<WorktreeInfo>> GetWorktreesAsync(CancellationToken ct = default) =>
        GitParsers.ParseWorktrees(await Git(ct, "worktree", "list", "--porcelain"));

    public async Task<CommitDetail?> GetCommitDetailAsync(string hash, CancellationToken ct = default) =>
        GitParsers.ParseDetail(await Git(ct, "show", "-s", $"--format={GitParsers.DetailFormat}", hash, "--"));

    /// <summary>True when the working tree or index differs from HEAD, including untracked files.</summary>
    public async Task<int> CountUncommittedAsync(CancellationToken ct = default)
    {
        var o = await Git(ct, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        var count = 0;
        var parts = o.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length < 3) continue;
            count++;
            // Rename entries carry the original path in the next field.
            if (parts[i][0] is 'R' or 'C') i++;
        }
        return count;
    }

    public async Task<List<FileChange>> GetChangesAsync(DiffTarget target, CancellationToken ct = default)
    {
        var range = target.To is null ? new[] { target.From } : new[] { target.From, target.To };
        var nameStatus = await Git(ct, ["diff", "--name-status", "-z", "-M", .. range, "--"]);
        var numstat = await Git(ct, ["diff", "--numstat", "-z", "-M", .. range, "--"]);
        var list = GitParsers.ParseChanges(nameStatus, numstat);
        if (target.IsWorkingTree)
        {
            var untracked = await Git(ct, "ls-files", "--others", "--exclude-standard", "-z");
            foreach (var p in GitParsers.SplitZ(untracked))
                list.Add(new FileChange(ChangeKind.Untracked, p, null, null, null));
        }
        return list;
    }

    /// <summary>Resolves what a commit row compares against: its first parent, or the empty tree for a root commit.</summary>
    public static DiffTarget TargetForCommit(CommitInfo c) =>
        new(c.Parents.Count > 0 ? c.Parents[0] : EmptyTree, c.Hash);

    /// <summary>
    /// The file before and after the change, for diffing in-process. A side that doesn't exist
    /// (an added file's old version, a deleted file's new version) is empty.
    /// </summary>
    public async Task<(string Old, string New)> GetFileVersionsAsync(DiffTarget target, FileChange file, CancellationToken ct = default)
    {
        var oldTask = file.Kind is ChangeKind.Added or ChangeKind.Untracked || target.From == EmptyTree
            ? Task.FromResult("")
            : ShowAsync(target.From, file.OldPath ?? file.Path, ct);
        string newText;
        if (file.Kind == ChangeKind.Deleted)
            newText = "";
        else if (target.To is null)
        {
            var full = System.IO.Path.Combine(Path, file.Path);
            newText = File.Exists(full) ? await File.ReadAllTextAsync(full, ct) : "";
        }
        else
            newText = await ShowAsync(target.To, file.Path, ct);
        return (await oldTask, newText);
    }

    private Task<string> ShowAsync(string rev, string path, CancellationToken ct) =>
        GitRunner.RunAsync(Path, ["show", "--no-textconv", $"{rev}:{path}"], ct);

    // ---- Commands ----------------------------------------------------------------------------

    public Task CheckoutAsync(string target, CancellationToken ct = default) => Git(ct, "checkout", target);

    public Task CreateBranchAsync(string name, string startPoint, bool checkout, CancellationToken ct = default) =>
        checkout ? Git(ct, "checkout", "-b", name, startPoint) : Git(ct, "branch", name, startPoint);

    public Task DeleteBranchAsync(string name, bool force, CancellationToken ct = default) =>
        Git(ct, "branch", force ? "-D" : "-d", name);

    public Task CreateTagAsync(string name, string target, string? message, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(message)
            ? Git(ct, "tag", name, target)
            : Git(ct, "tag", "-a", name, "-m", message, target);

    public Task DeleteTagAsync(string name, CancellationToken ct = default) => Git(ct, "tag", "-d", name);

    /// <summary>Merges <paramref name="target"/> into the checked-out branch, with git's default message.</summary>
    public Task MergeAsync(string target, bool noFastForward, bool squash, CancellationToken ct = default)
    {
        var args = new List<string> { "merge", "--no-edit" };
        if (squash) args.Add("--squash");
        else if (noFastForward) args.Add("--no-ff");
        args.Add(target);
        return GitRunner.RunAsync(Path, args, ct);
    }

    /// <summary>Applies one commit on top of HEAD. A merge commit is taken relative to its first parent.</summary>
    public Task CherryPickAsync(string hash, bool isMerge, bool recordOrigin, bool noCommit, CancellationToken ct = default)
    {
        var args = new List<string> { "cherry-pick" };
        if (isMerge) args.AddRange(["-m", "1"]);
        if (recordOrigin) args.Add("-x");
        if (noCommit) args.Add("--no-commit");
        args.Add(hash);
        return GitRunner.RunAsync(Path, args, ct);
    }

    /// <summary>Whether a merge or cherry-pick stopped part-way, for example on conflicts.</summary>
    public async Task<bool> IsInProgressAsync(GitOperation op, CancellationToken ct = default)
    {
        var gitDir = await GetGitDirAsync(ct);
        var marker = op == GitOperation.Merge ? "MERGE_HEAD" : "CHERRY_PICK_HEAD";
        return gitDir != null && File.Exists(System.IO.Path.Combine(gitDir, marker));
    }

    public Task AbortAsync(GitOperation op, CancellationToken ct = default) =>
        Git(ct, op == GitOperation.Merge ? "merge" : "cherry-pick", "--abort");

    /// <param name="newBranch">When set, creates this branch at <paramref name="commitish"/> and checks it out in the new worktree.</param>
    public Task AddWorktreeAsync(string worktreePath, string commitish, string? newBranch, bool detach, CancellationToken ct = default)
    {
        var args = new List<string> { "worktree", "add" };
        if (!string.IsNullOrWhiteSpace(newBranch)) { args.Add("-b"); args.Add(newBranch); }
        else if (detach) args.Add("--detach");
        args.Add(worktreePath);
        args.Add(commitish);
        return GitRunner.RunAsync(Path, args, ct);
    }

    public Task RemoveWorktreeAsync(string worktreePath, bool force, CancellationToken ct = default) =>
        force ? Git(ct, "worktree", "remove", "--force", worktreePath) : Git(ct, "worktree", "remove", worktreePath);

    public Task PruneWorktreesAsync(CancellationToken ct = default) => Git(ct, "worktree", "prune");

    public Task FetchAsync(CancellationToken ct = default) => Git(ct, "fetch", "--all", "--prune");

    /// <summary>Changed, untracked and ignored paths, relative to the top level. Untracked and ignored folders come as "dir/".</summary>
    public async Task<List<FileStatusEntry>> GetStatusEntriesAsync(CancellationToken ct = default)
    {
        var r = await GitRunner.RunRawAsync(Path, ["status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignored=matching"], ct);
        return r.ExitCode == 0 ? GitParsers.ParseStatus(r.StdOut) : [];
    }

    /// <summary>
    /// Non-merge commits reachable from HEAD, leaving out those reachable from <paramref name="exclude"/>.
    /// Without <paramref name="withLines"/> this is quick; counting lines makes git diff every commit, which
    /// takes seconds on a large history. Empty for a repository with no commits.
    /// </summary>
    public async Task<List<Stats.CommitStat>> GetCommitStatsAsync(bool withLines, IEnumerable<string>? exclude = null, CancellationToken ct = default)
    {
        var args = new List<string> { "log", "HEAD", "--no-merges", "--use-mailmap", $"--format={GitParsers.StatFormat}" };
        if (withLines) args.AddRange(["--shortstat", "--no-renames"]);
        if (exclude != null) args.AddRange(exclude.Select(h => "^" + h));
        var r = await GitRunner.RunRawAsync(Path, args, ct);
        return r.ExitCode == 0 ? await Task.Run(() => GitParsers.ParseStats(r.StdOut, withLines), ct) : [];
    }

    public async Task<SyncStatus> GetSyncStatusAsync(CancellationToken ct = default)
    {
        var branchTask = GitRunner.RunRawAsync(Path, ["symbolic-ref", "-q", "--short", "HEAD"], ct);
        var upstreamTask = GitRunner.RunRawAsync(Path, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], ct);
        var remotesTask = GitRunner.RunRawAsync(Path, ["remote"], ct);
        await Task.WhenAll(branchTask, upstreamTask, remotesTask);

        var branch = branchTask.Result.ExitCode == 0 ? branchTask.Result.StdOut.Trim() : null;
        var upstream = upstreamTask.Result.ExitCode == 0 ? upstreamTask.Result.StdOut.Trim() : null;
        var remotes = remotesTask.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int ahead = 0, behind = 0;
        if (branch != null && upstream != null)
        {
            var counts = await GitRunner.RunRawAsync(Path, ["rev-list", "--left-right", "--count", "HEAD...@{upstream}"], ct);
            var parts = counts.StdOut.Split('\t', StringSplitOptions.TrimEntries);
            if (counts.ExitCode == 0 && parts.Length == 2)
            {
                int.TryParse(parts[0], out ahead);
                int.TryParse(parts[1], out behind);
            }
        }
        return new SyncStatus(branch, upstream, ahead, behind, remotes);
    }

    /// <summary>
    /// Pulls into the checked-out branch the way git is configured to (pull.rebase / pull.ff). Without either
    /// setting, git refuses to reconcile diverged branches, so this falls back to a merge, git's old default.
    /// </summary>
    public async Task PullAsync(CancellationToken ct = default)
    {
        var rebase = await GitRunner.RunRawAsync(Path, ["config", "--get", "pull.rebase"], ct);
        var ff = await GitRunner.RunRawAsync(Path, ["config", "--get", "pull.ff"], ct);
        var configured = rebase.ExitCode == 0 || ff.ExitCode == 0;
        await (configured ? Git(ct, "pull", "--no-edit") : Git(ct, "pull", "--no-edit", "--no-rebase"));
    }

    public Task PushAsync(CancellationToken ct = default) => Git(ct, "push");

    /// <summary>Pushes a branch that has no upstream yet and starts tracking it, like VS Code's "Publish Branch".</summary>
    public Task PublishAsync(string remote, string branch, CancellationToken ct = default) =>
        Git(ct, "push", "--set-upstream", remote, branch);
}
