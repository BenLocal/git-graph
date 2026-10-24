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
    /// <param name="filter">Only commits by an author, since a date, or touching a path.</param>
    public async Task<List<CommitInfo>> GetCommitsAsync(int maxCount, IReadOnlyList<string>? branches, bool includeRemotes,
        CommitFilter? filter = null, CancellationToken ct = default)
    {
        var args = new List<string> { "log", "--date-order", $"--max-count={maxCount}", $"--format={GitParsers.LogFormat}" };
        if (filter is { Author: { Length: > 0 } author })
            args.AddRange(["--regexp-ignore-case", "--fixed-strings", $"--author={author}"]);
        if (filter is { Since: { } since }) args.Add($"--since={since.ToUnixTimeSeconds()}");
        // Literal text, case included; with an author filter too, git's one --regexp-ignore-case covers both.
        if (filter is { Code: { Length: > 0 } code } && !string.IsNullOrWhiteSpace(code)) args.Add($"-S{code}");
        // Limiting to a path simplifies history; --parents rewrites each commit's parents to the nearest ones shown.
        if (filter is { Path: { Length: > 0 } }) args.Add("--parents");
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
        if (filter is { Path: { Length: > 0 } path }) args.Add(path);
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

    /// <summary>
    /// How many paths differ from HEAD (in the index or the working tree, untracked files included), and the
    /// untracked ones: finding those is the slow part of status, so <see cref="GetWorkingChangesAsync"/> can reuse them.
    /// </summary>
    public async Task<UncommittedSummary> GetUncommittedAsync(CancellationToken ct = default)
    {
        var o = await Git(ct, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        var count = 0;
        var untracked = new List<string>();
        var parts = o.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length < 3) continue;
            count++;
            if (parts[i].StartsWith("?? ", StringComparison.Ordinal)) untracked.Add(parts[i][3..]);
            // Rename entries carry the original path in the next field.
            if (parts[i][0] is 'R' or 'C') i++;
        }
        return new UncommittedSummary(count, untracked);
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

    /// <summary>Stands for the index (the staging area) in a <see cref="DiffTarget"/>, as in <c>git show :path</c>.</summary>
    public const string Index = ":";

    /// <summary>
    /// The working tree's changes as git's staging sees them: staged (HEAD → index), unstaged (index → working
    /// tree, with untracked files) and conflicted. A file can be both staged and unstaged.
    /// </summary>
    /// <param name="untracked">Untracked files from a <see cref="GetUncommittedAsync"/> just made, to skip finding them again.</param>
    public async Task<WorkingChanges> GetWorkingChangesAsync(IReadOnlyList<string>? untracked = null, CancellationToken ct = default)
    {
        // Without HEAD (no commits yet), "diff --cached" compares the index with the empty tree.
        var stagedStatus = Git(ct, "diff", "--cached", "--name-status", "-z", "-M", "--");
        var stagedStat = Git(ct, "diff", "--cached", "--numstat", "-z", "-M", "--");
        var unstagedStatus = Git(ct, "diff", "--name-status", "-z", "--");
        var unstagedStat = Git(ct, "diff", "--numstat", "-z", "--");
        var untrackedTask = untracked is null ? Git(ct, "ls-files", "--others", "--exclude-standard", "-z") : Task.FromResult("");
        await Task.WhenAll(stagedStatus, stagedStat, unstagedStatus, unstagedStat, untrackedTask);

        var staged = GitParsers.ParseChanges(stagedStatus.Result, stagedStat.Result);
        var unstaged = GitParsers.ParseChanges(unstagedStatus.Result, unstagedStat.Result);
        foreach (var p in untracked ?? GitParsers.SplitZ(untrackedTask.Result).ToList())
            unstaged.Add(new FileChange(ChangeKind.Untracked, p, null, null, null));
        // An unmerged file shows on both sides; it belongs only with the conflicts.
        var conflicted = staged.Concat(unstaged).Where(c => c.Kind == ChangeKind.Conflicted).DistinctBy(c => c.Path).ToList();
        var conflictPaths = conflicted.Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
        return new WorkingChanges(
            staged.Where(c => !conflictPaths.Contains(c.Path)).ToList(),
            unstaged.Where(c => !conflictPaths.Contains(c.Path)).ToList(),
            conflicted);
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

    /// <summary>Both versions of a file as bytes, for showing images; a side that doesn't exist is null.</summary>
    public async Task<(byte[]? Old, byte[]? New)> GetFileBytesAsync(DiffTarget target, FileChange file, CancellationToken ct = default)
    {
        Task<byte[]> Show(string rev, string path) =>
            GitRunner.RunBytesAsync(Path, ["show", "--no-textconv", rev == Index ? ":" + path : $"{rev}:{path}"], ct);

        var oldBytes = file.Kind is ChangeKind.Added or ChangeKind.Untracked || target.From == EmptyTree
            ? null
            : await Show(target.From, file.OldPath ?? file.Path);
        byte[]? newBytes = null;
        if (file.Kind != ChangeKind.Deleted)
        {
            if (target.To is null)
            {
                var full = System.IO.Path.Combine(Path, file.Path);
                newBytes = File.Exists(full) ? await File.ReadAllBytesAsync(full, ct) : null;
            }
            else newBytes = await Show(target.To, file.Path);
        }
        return (oldBytes, newBytes);
    }

    private Task<string> ShowAsync(string rev, string path, CancellationToken ct) =>
        GitRunner.RunAsync(Path, ["show", "--no-textconv", rev == Index ? ":" + path : $"{rev}:{path}"], ct);

    // ---- Commands ----------------------------------------------------------------------------

    /// <summary>Commits that changed a file, newest first, following it through renames.</summary>
    /// <param name="rev">Start from this commit instead of HEAD.</param>
    public async Task<List<FileHistoryEntry>> GetFileHistoryAsync(string path, string? rev, int maxCount, CancellationToken ct = default)
    {
        var args = new List<string> { "log", "--follow", "-M", "--name-status", "-z", $"--format={GitParsers.FileHistoryFormat}", $"--max-count={maxCount}" };
        if (rev != null) args.Add(rev);
        args.AddRange(["--", path]);
        var r = await GitRunner.RunRawAsync(Path, args, ct);
        // A file in a repository with no commits yet has no history.
        return r.ExitCode == 0 ? GitParsers.ParseFileHistory(r.StdOut) : [];
    }

    /// <summary>Who last changed each line: of the working tree's file, or of the file at <paramref name="rev"/>.</summary>
    public async Task<BlameResult> BlameAsync(string path, string? rev, CancellationToken ct = default)
    {
        var args = new List<string> { "blame", "--porcelain" };
        if (rev != null) args.Add(rev);
        args.AddRange(["--", path]);
        return GitParsers.ParseBlame(await GitRunner.RunAsync(Path, args, ct));
    }

    // ---- Stash -------------------------------------------------------------------------------

    public async Task<List<StashEntry>> GetStashesAsync(CancellationToken ct = default)
    {
        var r = await GitRunner.RunRawAsync(Path, ["stash", "list", $"--format={GitParsers.StashFormat}"], ct);
        return r.ExitCode == 0 ? GitParsers.ParseStashes(r.StdOut) : [];
    }

    /// <summary>Untracked files a stash saved, which live in its third parent rather than in the stash itself.</summary>
    public async Task<List<string>> GetStashUntrackedAsync(StashEntry stash, CancellationToken ct = default) =>
        stash.HasUntracked ? GitParsers.SplitZ(await Git(ct, "ls-tree", "-r", "-z", "--name-only", stash.Hash + "^3")).ToList() : [];

    /// <summary>Saves the uncommitted changes as a new stash and cleans the working tree.</summary>
    public Task StashAsync(string? message, bool includeUntracked, CancellationToken ct = default)
    {
        var args = new List<string> { "stash", "push" };
        if (includeUntracked) args.Add("--include-untracked");
        if (!string.IsNullOrWhiteSpace(message)) { args.Add("-m"); args.Add(message.Trim()); }
        return GitRunner.RunAsync(Path, args, ct);
    }

    /// <param name="pop">Drop the stash once it applied cleanly.</param>
    /// <param name="restoreIndex">Restore what was staged as staged, instead of leaving everything unstaged.</param>
    public Task ApplyStashAsync(string name, bool pop, bool restoreIndex, CancellationToken ct = default) =>
        restoreIndex ? Git(ct, "stash", pop ? "pop" : "apply", "--index", name) : Git(ct, "stash", pop ? "pop" : "apply", name);

    public Task DropStashAsync(string name, CancellationToken ct = default) => Git(ct, "stash", "drop", name);

    /// <summary>Creates a branch at the commit the stash was made on, checks it out, applies the stash and drops it.</summary>
    public Task StashBranchAsync(string branch, string name, CancellationToken ct = default) => Git(ct, "stash", "branch", branch, name);

    /// <summary>Stages files as they are in the working tree, deletions included.</summary>
    public Task StageAsync(IEnumerable<string> paths, CancellationToken ct = default) =>
        GitRunner.RunAsync(Path, ["add", "-A", "--", .. paths], ct);

    /// <summary>Takes files out of the index again, leaving the working tree as it is.</summary>
    public async Task UnstageAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        // With no commits yet there is nothing to restore from: the files just leave the index.
        if ((await GetHeadAsync(ct)).Hash is null) await GitRunner.RunAsync(Path, ["rm", "--cached", "-r", "-q", "--", .. paths], ct);
        else await GitRunner.RunAsync(Path, ["restore", "--staged", "--", .. paths], ct);
    }

    /// <summary>
    /// Throws away unstaged changes: tracked files go back to their staged (or committed) content and untracked
    /// files are deleted.
    /// </summary>
    public async Task DiscardAsync(IReadOnlyCollection<FileChange> files, CancellationToken ct = default)
    {
        var untracked = files.Where(f => f.Kind == ChangeKind.Untracked).Select(f => f.Path).ToList();
        var tracked = files.Where(f => f.Kind != ChangeKind.Untracked).Select(f => f.Path).ToList();
        if (tracked.Count > 0) await GitRunner.RunAsync(Path, ["restore", "--worktree", "--", .. tracked], ct);
        if (untracked.Count > 0) await GitRunner.RunAsync(Path, ["clean", "-f", "-q", "--", .. untracked], ct);
    }

    /// <param name="amend">Replace the last commit with the index and this message instead of adding one.</param>
    public Task CommitAsync(string message, bool amend, CancellationToken ct = default) =>
        amend ? Git(ct, "commit", "--amend", "-m", message) : Git(ct, "commit", "-m", message);

    public async Task<string> GetLastCommitMessageAsync(CancellationToken ct = default) =>
        (await Git(ct, "log", "-1", "--format=%B")).TrimEnd();

    /// <summary>
    /// Applies a patch to the index (<paramref name="cached"/>) or the working tree; <paramref name="reverse"/> takes
    /// it back out. Hunk counts are recounted, so a patch built from one hunk of a larger diff applies cleanly.
    /// </summary>
    public async Task ApplyPatchAsync(string patch, bool cached, bool reverse, CancellationToken ct = default)
    {
        var file = System.IO.Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, patch, ct);
            var args = new List<string> { "apply", "--recount", "--whitespace=nowarn" };
            if (cached) args.Add("--cached");
            if (reverse) args.Add("-R");
            args.Add(file);
            await GitRunner.RunAsync(Path, args, ct);
        }
        finally { File.Delete(file); }
    }

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

    public Task RenameBranchAsync(string name, string newName, CancellationToken ct = default) => Git(ct, "branch", "-m", name, newName);

    /// <param name="upstream">A remote branch such as "origin/main".</param>
    public Task SetUpstreamAsync(string branch, string upstream, CancellationToken ct = default) =>
        Git(ct, "branch", $"--set-upstream-to={upstream}", branch);

    /// <param name="branch">The branch's name on the remote: "main", not "origin/main".</param>
    public Task DeleteRemoteBranchAsync(string remote, string branch, CancellationToken ct = default) =>
        Git(ct, "push", remote, "--delete", branch);

    public Task PushTagAsync(string remote, string tag, CancellationToken ct = default) => Git(ct, "push", remote, $"refs/tags/{tag}");

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

    /// <summary>Undoes a commit with a new commit. A merge commit is undone relative to its first parent.</summary>
    public Task RevertAsync(string hash, bool isMerge, bool noCommit, CancellationToken ct = default)
    {
        var args = new List<string> { "revert", "--no-edit" };
        if (isMerge) args.AddRange(["-m", "1"]);
        if (noCommit) args.Add("--no-commit");
        args.Add(hash);
        return GitRunner.RunAsync(Path, args, ct);
    }

    /// <summary>Moves the checked-out branch to <paramref name="target"/>.</summary>
    public Task ResetAsync(string target, ResetMode mode, CancellationToken ct = default) =>
        Git(ct, "reset", mode switch { ResetMode.Soft => "--soft", ResetMode.Hard => "--hard", _ => "--mixed" }, target);

    /// <summary>The last moves of <paramref name="refName"/> (HEAD, or refs/heads/…), newest first.</summary>
    public async Task<List<ReflogEntry>> GetReflogAsync(string refName = "HEAD", int maxCount = 200, CancellationToken ct = default)
    {
        var r = await GitRunner.RunRawAsync(Path, ["reflog", "show", "--date=unix", $"--format={GitParsers.ReflogFormat}", $"-n{maxCount}", refName, "--"], ct);
        // A branch or repository without a reflog yet has nothing to list.
        return r.ExitCode == 0 ? GitParsers.ParseReflog(r.StdOut) : [];
    }

    /// <summary>
    /// How to take back the last thing that moved HEAD, as lazygit's undo does: a checkout switches back; otherwise the
    /// branch returns to where its reflog says it was before. A commit or amend comes back off the branch with its
    /// changes staged; anything else (merge, rebase, pull, reset, cherry-pick, revert) moves the files back too and
    /// keeps uncommitted changes. Undoing an undo redoes it. Null when there is nothing before.
    /// </summary>
    public async Task<UndoStep?> GetUndoAsync(CancellationToken ct = default)
    {
        var headLog = await GetReflogAsync("HEAD", 1, ct);
        if (headLog.Count == 0) return null;
        if (headLog[0] is { Action: "checkout" } checkout && System.Text.RegularExpressions.Regex.Match(checkout.Message, "^moving from (.+) to .+$") is { Success: true } m)
            return new UndoStep(UndoKind.Checkout, m.Groups[1].Value, checkout.Action, checkout.Message, null);

        // On a branch its own reflog has one entry per change (a rebase is one), where HEAD's has one per step.
        var branch = (await GetHeadAsync(ct)).Branch;
        var log = await GetReflogAsync(branch is null ? "HEAD" : $"refs/heads/{branch}", 2, ct);
        if (log.Count < 2) return null;
        var (last, target) = (log[0], log[1].Hash);
        // When the index already holds the target's files (redoing a commit undone with its changes staged), only the
        // branch needs to move; a keep reset would refuse because of those staged changes.
        var indexIsTarget = (await GitRunner.RunRawAsync(Path, ["diff", "--cached", "--quiet", target, "--"], ct)).ExitCode == 0;
        var kind = last.Action is "commit" or "commit (amend)" || indexIsTarget ? UndoKind.SoftReset : UndoKind.KeepReset;
        return new UndoStep(kind, target, last.Action, last.Message, branch);
    }

    public Task UndoAsync(UndoStep step, CancellationToken ct = default) => step.Kind switch
    {
        UndoKind.Checkout => Git(ct, "checkout", step.Target),
        UndoKind.SoftReset => Git(ct, "reset", "--soft", step.Target),
        _ => Git(ct, "reset", "--keep", step.Target),
    };

    public async Task<bool> IsAncestorAsync(string ancestor, string of, CancellationToken ct = default) =>
        (await GitRunner.RunRawAsync(Path, ["merge-base", "--is-ancestor", ancestor, of], ct)).ExitCode == 0;

    /// <summary>The commits an interactive rebase onto <paramref name="baseHash"/> replays, oldest first, with their messages.</summary>
    public async Task<List<RebaseCommit>> GetRebaseCommitsAsync(string baseHash, CancellationToken ct = default) =>
        GitParsers.ParseRebaseCommits(await Git(ct, "log", "--reverse", "--topo-order", $"--format={GitParsers.RebaseFormat}", $"{baseHash}..HEAD", "--"));

    /// <summary>How many commits after <paramref name="baseHash"/> the branch's upstream already has: rewriting them needs a force push.</summary>
    public async Task<int> CountPushedAsync(string baseHash, CancellationToken ct = default)
    {
        var upstream = await GitRunner.RunRawAsync(Path, ["rev-parse", "--verify", "-q", "@{upstream}"], ct);
        if (upstream.ExitCode != 0) return 0;
        var pushed = await Git(ct, "rev-list", "--count", $"{baseHash}..HEAD", $"^{upstream.StdOut.Trim()}");
        var all = await Git(ct, "rev-list", "--count", $"{baseHash}..HEAD");
        return int.Parse(all.Trim()) - int.Parse(pushed.Trim());
    }

    /// <summary>
    /// Replays the commits after <paramref name="baseHash"/> as <paramref name="steps"/> say (oldest first), without an
    /// editor: git is handed the finished todo list, a reword amends the message from a file right after its pick,
    /// and a squash keeps both messages. Uncommitted changes are stashed and brought back. A conflict stops the rebase
    /// (GitException) for the user to continue or abort, as with any rebase.
    /// </summary>
    public async Task InteractiveRebaseAsync(string baseHash, IReadOnlyList<RebaseStep> steps, CancellationToken ct = default)
    {
        // The files must outlive a rebase that stops on a conflict, since its exec lines run after it continues; those
        // left by such rebases go once they are a day old.
        foreach (var old in new DirectoryInfo(System.IO.Path.GetTempPath()).EnumerateDirectories("gitgraph-rebase-*"))
            if (old.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1))
                try { old.Delete(recursive: true); } catch (IOException) { }
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gitgraph-rebase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
        var todo = new System.Text.StringBuilder();
        var n = 0;
        foreach (var step in steps)
        {
            switch (step.Action)
            {
                case RebaseAction.Drop:
                    todo.Append("drop ").Append(step.Hash).Append('\n');
                    break;
                case RebaseAction.Reword:
                    var message = System.IO.Path.Combine(dir, $"message-{n++}.txt");
                    await File.WriteAllTextAsync(message, step.Message ?? "", ct);
                    todo.Append("pick ").Append(step.Hash).Append('\n');
                    todo.Append("exec git commit --amend --allow-empty --cleanup=strip -F ").Append(Quote(message)).Append('\n');
                    break;
                default:
                    todo.Append(step.Action.ToString().ToLowerInvariant()).Append(' ').Append(step.Hash).Append('\n');
                    break;
            }
        }
        var todoFile = System.IO.Path.Combine(dir, "git-rebase-todo");
        await File.WriteAllTextAsync(todoFile, todo.ToString(), ct);
        await Git(ct, "-c", $"sequence.editor=cp {Quote(todoFile)}", "-c", "core.editor=true", "rebase", "-i", "--autostash", baseHash);
        Directory.Delete(dir, recursive: true);
    }

    public async Task<bool> HasStagedChangesAsync(CancellationToken ct = default) =>
        (await GitRunner.RunRawAsync(Path, ["diff", "--cached", "--quiet", "--"], ct)).ExitCode == 1;

    /// <summary>Whether the checked-out branch's upstream already has <paramref name="hash"/>, so rewriting it needs a force push.</summary>
    public async Task<bool> IsPushedAsync(string hash, CancellationToken ct = default)
    {
        var upstream = await GitRunner.RunRawAsync(Path, ["rev-parse", "--verify", "-q", "@{upstream}"], ct);
        return upstream.ExitCode == 0 && await IsAncestorAsync(hash, upstream.StdOut.Trim(), ct);
    }

    /// <summary>
    /// Folds the staged changes into <paramref name="target"/>, an earlier commit of the checked-out branch, as
    /// lazygit's "amend to commit" does: a fixup commit, then an autosquash rebase that needs no editor. Into HEAD
    /// itself it is a plain amend. Unstaged changes are stashed and brought back; a conflict stops the rebase.
    /// </summary>
    public async Task FoldStagedIntoAsync(string target, CancellationToken ct = default)
    {
        if ((await GetHeadAsync(ct)).Hash == target)
        {
            await Git(ct, "commit", "--amend", "--no-edit");
            return;
        }
        await Git(ct, "commit", "--no-edit", $"--fixup={target}");
        var parent = await GitRunner.RunRawAsync(Path, ["rev-parse", "--verify", "-q", $"{target}^"], ct);
        await Git(ct, "-c", "sequence.editor=true", "-c", "core.editor=true", "rebase", "-i", "--autosquash", "--autostash",
            parent.ExitCode == 0 ? parent.StdOut.Trim() : "--root");
    }

    /// <summary>Replays the checked-out branch's own commits on top of <paramref name="onto"/>.</summary>
    /// <param name="autostash">Stash uncommitted changes first and bring them back afterwards.</param>
    public Task RebaseAsync(string onto, bool autostash, CancellationToken ct = default) =>
        autostash ? Git(ct, "rebase", "--autostash", onto) : Git(ct, "rebase", onto);

    /// <summary>The merge, cherry-pick, revert or rebase waiting to be continued or aborted, if any.</summary>
    public async Task<GitOperation?> GetOperationInProgressAsync(CancellationToken ct = default)
    {
        var gitDir = await GetGitDirAsync(ct);
        if (gitDir is null) return null;
        bool Has(string name) => File.Exists(System.IO.Path.Combine(gitDir, name)) || Directory.Exists(System.IO.Path.Combine(gitDir, name));
        // A rebase that stops on a conflict also leaves CHERRY_PICK_HEAD behind, so it is checked first.
        if (Has("rebase-merge") || Has("rebase-apply")) return GitOperation.Rebase;
        if (Has("MERGE_HEAD")) return GitOperation.Merge;
        if (Has("CHERRY_PICK_HEAD")) return GitOperation.CherryPick;
        if (Has("REVERT_HEAD")) return GitOperation.Revert;
        return null;
    }

    public Task AbortAsync(GitOperation op, CancellationToken ct = default) => Git(ct, Verb(op), "--abort");

    /// <summary>Goes on after the conflicts were resolved and staged, keeping the messages git prepared.</summary>
    public Task ContinueAsync(GitOperation op, CancellationToken ct = default) =>
        op == GitOperation.Merge
            ? Git(ct, "commit", "--no-edit")
            // No terminal to edit the message in: keep it as it is.
            : Git(ct, "-c", "core.editor=true", Verb(op), "--continue");

    private static string Verb(GitOperation op) => op switch
    {
        GitOperation.Merge => "merge",
        GitOperation.CherryPick => "cherry-pick",
        GitOperation.Revert => "revert",
        _ => "rebase",
    };

    /// <summary>Resolves conflicted files by taking one side whole (during a rebase, "ours" is the branch being rebased onto), and stages them.</summary>
    public async Task TakeSideAsync(IEnumerable<string> paths, bool ours, CancellationToken ct = default)
    {
        var list = paths.ToList();
        await GitRunner.RunAsync(Path, ["checkout", ours ? "--ours" : "--theirs", "--", .. list], ct);
        await StageAsync(list, ct);
    }

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

    public static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Fetches every remote. A fetch still running after <see cref="FetchTimeout"/> is stopped, so a stalled
    /// connection can't hold up every later fetch.
    /// </summary>
    public async Task FetchAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(FetchTimeout);
        try { await Git(timeout.Token, "fetch", "--all", "--prune"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GitException($"git fetch was still running after {FetchTimeout.TotalMinutes:0} minutes, so it was stopped. "
                + "Check the network connection and the remote's address.", -1, "fetch --all --prune");
        }
    }

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
    public async Task PullAsync(PullMode mode = PullMode.GitConfig, CancellationToken ct = default)
    {
        switch (mode)
        {
            case PullMode.Merge: await Git(ct, "pull", "--no-edit", "--no-rebase"); return;
            case PullMode.Rebase: await Git(ct, "pull", "--rebase"); return;
        }
        var rebase = await GitRunner.RunRawAsync(Path, ["config", "--get", "pull.rebase"], ct);
        var ff = await GitRunner.RunRawAsync(Path, ["config", "--get", "pull.ff"], ct);
        var configured = rebase.ExitCode == 0 || ff.ExitCode == 0;
        await (configured ? Git(ct, "pull", "--no-edit") : Git(ct, "pull", "--no-edit", "--no-rebase"));
    }

    /// <param name="forceWithLease">Overwrite the remote branch, but only if it is still where the last fetch saw it.</param>
    public Task PushAsync(bool forceWithLease = false, CancellationToken ct = default) =>
        forceWithLease ? Git(ct, "push", "--force-with-lease") : Git(ct, "push");

    /// <summary>Pushes a branch that has no upstream yet and starts tracking it, like VS Code's "Publish Branch".</summary>
    public Task PublishAsync(string remote, string branch, CancellationToken ct = default) =>
        Git(ct, "push", "--set-upstream", remote, branch);
}

/// <summary>How Pull brings in the upstream's commits.</summary>
public enum PullMode
{
    /// <summary>As pull.rebase / pull.ff say; a merge when neither is set.</summary>
    GitConfig,
    Merge,
    Rebase,
}
