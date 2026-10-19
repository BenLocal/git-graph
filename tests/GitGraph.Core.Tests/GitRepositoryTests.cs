using GitGraph.Core.Diff;
using GitGraph.Core.Git;
using GitGraph.Core.Workspaces;

namespace GitGraph.Core.Tests;

/// <summary>Runs the real git CLI against throwaway repositories.</summary>
public sealed class GitRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gitgraph-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;

    public GitRepositoryTests()
    {
        _repo = Path.Combine(_root, "app");
        Directory.CreateDirectory(_repo);
        Git("init", "-q", "-b", "main");
        Git("config", "user.name", "Tester");
        Git("config", "user.email", "t@example.com");
        Git("config", "commit.gpgsign", "false");
        Commit("a.txt", "one\n", "first");
        Git("checkout", "-q", "-b", "feature");
        Commit("b.txt", "feature\n", "feature work");
        Git("checkout", "-q", "main");
        Commit("a.txt", "one\ntwo\n", "second");
        Git("merge", "-q", "--no-ff", "-m", "merge feature", "feature");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private void Git(params string[] args) => GitRunner.RunAsync(_repo, args).GetAwaiter().GetResult();

    private void Commit(string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(_repo, file), content);
        Git("add", file);
        Git("commit", "-q", "-m", message);
    }

    [Fact]
    public async Task Commits_refs_and_head_are_read()
    {
        var repo = new GitRepository(_repo);
        var commits = await repo.GetCommitsAsync(100, null, true);
        var refs = await repo.GetRefsAsync();
        var head = await repo.GetHeadAsync();

        Assert.Equal(4, commits.Count);
        Assert.True(commits[0].IsMerge);
        Assert.Equal("merge feature", commits[0].Subject);
        Assert.Equal("main", head.Branch);
        Assert.Contains(refs, r => r is { Name: "feature", Kind: RefKind.LocalBranch });
    }

    [Fact]
    public async Task Changes_and_file_diff_for_a_commit()
    {
        var repo = new GitRepository(_repo);
        var second = (await repo.GetCommitsAsync(100, null, true)).Single(c => c.Subject == "second");
        var target = GitRepository.TargetForCommit(second);

        var changes = await repo.GetChangesAsync(target);
        var change = Assert.Single(changes);
        Assert.Equal(("a.txt", 1, 0), (change.Path, change.Additions, change.Deletions));

        var (oldText, newText) = await repo.GetFileVersionsAsync(target, change);
        Assert.Equal("one\n", oldText.Replace("\r", ""));
        Assert.Contains("two", newText);
    }

    [Fact]
    public async Task Merge_fast_forwards_or_creates_a_merge_commit()
    {
        Git("checkout", "-q", "-b", "topic");
        Commit("c.txt", "topic\n", "topic work");
        Git("checkout", "-q", "main");
        var repo = new GitRepository(_repo);

        await repo.MergeAsync("topic", noFastForward: true, squash: false);

        var parents = (await GitRunner.RunAsync(_repo, ["rev-list", "--parents", "-n", "1", "HEAD"])).Trim().Split(' ');
        Assert.Equal(3, parents.Length); // the merge commit and its two parents
    }

    [Fact]
    public async Task Cherry_pick_conflict_is_detected_and_aborted()
    {
        // Branch from "first" and change the same line that "second" changed on main.
        Git("checkout", "-q", "-b", "other", "main~2");
        Commit("a.txt", "one\nclash\n", "clashing change");
        var clash = (await GitRunner.RunAsync(_repo, ["rev-parse", "HEAD"])).Trim();
        Git("checkout", "-q", "main");
        var repo = new GitRepository(_repo);

        await Assert.ThrowsAsync<GitException>(() => repo.CherryPickAsync(clash, isMerge: false, recordOrigin: false, noCommit: false));
        Assert.True(await repo.IsInProgressAsync(GitOperation.CherryPick));

        await repo.AbortAsync(GitOperation.CherryPick);
        Assert.False(await repo.IsInProgressAsync(GitOperation.CherryPick));
        Assert.Equal(0, await repo.CountUncommittedAsync());
    }

    [Fact]
    public async Task Sync_status_counts_commits_to_push_and_pull()
    {
        var remote = Path.Combine(_root, "remote.git");
        Git("init", "-q", "--bare", "-b", "main", remote);
        Git("remote", "add", "origin", remote);
        var repo = new GitRepository(_repo);

        var unpublished = await repo.GetSyncStatusAsync();
        Assert.True(unpublished.CanPublish);
        await repo.PublishAsync("origin", "main");

        // One commit only here (to push), one only on the remote (to pull).
        var clone = Path.Combine(_root, "clone");
        Git("clone", "-q", remote, clone);
        await GitRunner.RunAsync(clone, ["-c", "user.name=T", "-c", "user.email=t@example.com", "commit", "-q", "--allow-empty", "-m", "theirs"]);
        await GitRunner.RunAsync(clone, ["push", "-q"]);
        Commit("d.txt", "mine\n", "mine");
        await repo.FetchAsync();

        var status = await repo.GetSyncStatusAsync();
        Assert.Equal(("main", "origin/main", 1, 1), (status.Branch, status.Upstream, status.Ahead, status.Behind));

        await repo.PullAsync();
        await repo.PushAsync();
        var synced = await repo.GetSyncStatusAsync();
        Assert.Equal((0, 0), (synced.Ahead, synced.Behind));
    }

    [Fact]
    public async Task Pull_can_rebase_and_push_can_force_with_lease()
    {
        var remote = Path.Combine(_root, "remote.git");
        Git("init", "-q", "--bare", "-b", "main", remote);
        Git("remote", "add", "origin", remote);
        var repo = new GitRepository(_repo);
        await repo.PublishAsync("origin", "main");
        var clone = Path.Combine(_root, "clone");
        Git("clone", "-q", remote, clone);
        string[] identity = ["-c", "user.name=T", "-c", "user.email=t@example.com"];
        await GitRunner.RunAsync(clone, [.. identity, "commit", "-q", "--allow-empty", "-m", "theirs"]);
        await GitRunner.RunAsync(clone, ["push", "-q"]);

        // Rebasing puts "mine" on top of "theirs" with no merge commit.
        Commit("d.txt", "mine\n", "mine");
        await repo.PullAsync(PullMode.Rebase);
        var parents = await GitRunner.RunAsync(_repo, ["log", "-1", "--format=%P"]);
        Assert.Single(parents.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        await repo.PushAsync();

        // A rewritten commit needs a forced push...
        Git("commit", "-q", "--amend", "-m", "mine, reworded");
        await Assert.ThrowsAsync<GitException>(() => repo.PushAsync());
        await repo.PushAsync(forceWithLease: true);

        // ...which is refused when the remote moved since the last fetch.
        await GitRunner.RunAsync(clone, ["pull", "-q", "--rebase"]);
        await GitRunner.RunAsync(clone, [.. identity, "commit", "-q", "--allow-empty", "-m", "theirs again"]);
        await GitRunner.RunAsync(clone, ["push", "-q"]);
        Git("commit", "-q", "--amend", "-m", "mine, reworded twice");
        await Assert.ThrowsAsync<GitException>(() => repo.PushAsync(forceWithLease: true));
    }

    [Fact]
    public async Task Staging_unstaging_discarding_and_committing()
    {
        var repo = new GitRepository(_repo);
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "one\ntwo\nthree\n");
        await repo.StageAsync(["a.txt"]);
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "one\ntwo\nthree\nfour\n");
        File.WriteAllText(Path.Combine(_repo, "new.txt"), "new\n");
        File.Delete(Path.Combine(_repo, "b.txt"));

        var changes = await repo.GetWorkingChangesAsync();
        Assert.Equal(["a.txt"], changes.Staged.Select(c => c.Path));
        Assert.Equal([("a.txt", ChangeKind.Modified), ("b.txt", ChangeKind.Deleted), ("new.txt", ChangeKind.Untracked)],
            changes.Unstaged.Select(c => (c.Path, c.Kind)).OrderBy(c => c.Path));

        // The staged side of a.txt is the index; the unstaged side is index → working tree.
        var file = changes.Staged[0];
        var (oldText, newText) = await repo.GetFileVersionsAsync(new DiffTarget("HEAD", GitRepository.Index), file);
        Assert.Equal(("one\ntwo\n", "one\ntwo\nthree\n"), (oldText, newText));

        await repo.UnstageAsync(["a.txt"]);
        await repo.DiscardAsync(changes.Unstaged.Where(c => c.Path != "a.txt").ToList());
        changes = await repo.GetWorkingChangesAsync();
        Assert.Empty(changes.Staged);
        Assert.Equal(["a.txt"], changes.Unstaged.Select(c => c.Path));
        Assert.True(File.Exists(Path.Combine(_repo, "b.txt")));
        Assert.False(File.Exists(Path.Combine(_repo, "new.txt")));

        await repo.StageAsync(["a.txt"]);
        await repo.CommitAsync("four lines", amend: false);
        await repo.CommitAsync("four lines, reworded", amend: true);
        Assert.Equal("four lines, reworded", await repo.GetLastCommitMessageAsync());
        Assert.Equal(0, (await repo.GetWorkingChangesAsync()).Count);
    }

    [Fact]
    public async Task One_hunk_can_be_staged_unstaged_or_discarded()
    {
        var repo = new GitRepository(_repo);
        // Tabs, CRLF endings and no final newline must all survive the patch.
        var lines = Enumerable.Range(1, 20).Select(i => $"\tline {i}").ToArray();
        string Text(string[] l) => string.Join("\r\n", l);
        File.WriteAllText(Path.Combine(_repo, "f.txt"), Text(lines));
        Git("add", "f.txt");
        Git("commit", "-q", "-m", "twenty lines");
        var changed = (string[])lines.Clone();
        changed[1] = "\tline 2, changed";
        changed[19] = "\tline 20, changed";
        File.WriteAllText(Path.Combine(_repo, "f.txt"), Text(changed));

        async Task<(string Old, string New, FileDiff Diff)> Unstaged()
        {
            var (o, n) = await repo.GetFileVersionsAsync(new DiffTarget(GitRepository.Index, null), new FileChange(ChangeKind.Modified, "f.txt", null, 1, 1));
            return (o, n, TextDiff.Build(o, n));
        }

        var (oldText, newText, diff) = await Unstaged();
        Assert.Equal(2, diff.Lines.Count(l => l.Kind == DiffLineKind.Hunk));
        await repo.ApplyPatchAsync(TextDiff.HunkPatch("f.txt", oldText, newText, diff, 0), cached: true, reverse: false);
        var staged = await GitRunner.RunAsync(_repo, ["diff", "--cached"]);
        Assert.Contains("+\tline 2, changed", staged);
        Assert.DoesNotContain("line 20, changed", staged);

        // The second hunk is the last line, with no newline after it.
        (oldText, newText, diff) = await Unstaged();
        await repo.ApplyPatchAsync(TextDiff.HunkPatch("f.txt", oldText, newText, diff, 0), cached: true, reverse: false);
        Assert.Empty(await GitRunner.RunAsync(_repo, ["diff"]));

        // Unstage the first hunk again (HEAD → index, reversed), then discard it from the working tree.
        var (headText, indexText) = await repo.GetFileVersionsAsync(new DiffTarget("HEAD", GitRepository.Index), new FileChange(ChangeKind.Modified, "f.txt", null, 2, 2));
        await repo.ApplyPatchAsync(TextDiff.HunkPatch("f.txt", headText, indexText, TextDiff.Build(headText, indexText), 0), cached: true, reverse: true);
        (oldText, newText, diff) = await Unstaged();
        await repo.ApplyPatchAsync(TextDiff.HunkPatch("f.txt", oldText, newText, diff, 0), cached: false, reverse: true);
        Assert.Empty(await GitRunner.RunAsync(_repo, ["diff"]));
        changed[1] = lines[1];
        Assert.Equal(Text(changed), File.ReadAllText(Path.Combine(_repo, "f.txt")));
    }

    [Fact]
    public async Task Stashes_are_listed_applied_dropped_and_branched()
    {
        var repo = new GitRepository(_repo);
        var head = (await repo.GetHeadAsync()).Hash;
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "changed\n");
        File.WriteAllText(Path.Combine(_repo, "loose.txt"), "untracked\n");
        await repo.StashAsync("first try", includeUntracked: true);
        Assert.Equal(0, (await repo.GetWorkingChangesAsync()).Count);

        File.WriteAllText(Path.Combine(_repo, "a.txt"), "again\n");
        await repo.StashAsync(null, includeUntracked: false);

        var stashes = await repo.GetStashesAsync();
        Assert.Equal(["stash@{0}", "stash@{1}"], stashes.Select(x => x.Name));
        Assert.All(stashes, x => Assert.Equal(head, x.BaseHash));
        Assert.Equal("On main: first try", stashes[1].Message);
        Assert.StartsWith("WIP on main:", stashes[0].Message);
        Assert.Equal((false, true), (stashes[0].HasUntracked, stashes[1].HasUntracked));
        Assert.Equal(["loose.txt"], await repo.GetStashUntrackedAsync(stashes[1]));
        Assert.Equal(["a.txt"], (await repo.GetChangesAsync(new DiffTarget(stashes[1].BaseHash, stashes[1].Hash))).Select(c => c.Path));

        // Apply keeps the stash, pop removes it; the older stash moves up to stash@{0}.
        await repo.ApplyStashAsync("stash@{0}", pop: false, restoreIndex: false);
        Assert.Equal("again\n", File.ReadAllText(Path.Combine(_repo, "a.txt")));
        await repo.DiscardAsync((await repo.GetWorkingChangesAsync()).Unstaged);
        await repo.DropStashAsync("stash@{0}");
        await repo.StashBranchAsync("from-stash", "stash@{0}");
        Assert.Empty(await repo.GetStashesAsync());
        Assert.Equal("from-stash", (await repo.GetHeadAsync()).Branch);
        Assert.True(File.Exists(Path.Combine(_repo, "loose.txt")));
    }

    [Fact]
    public async Task File_history_follows_renames_and_blame_names_each_line()
    {
        var repo = new GitRepository(_repo);
        Git("mv", "a.txt", "renamed.txt");
        Git("commit", "-q", "-m", "rename a");
        Commit("renamed.txt", "one\ntwo\nthree\n", "add three");

        var history = await repo.GetFileHistoryAsync("renamed.txt", null, 50);
        Assert.Equal(["add three", "rename a", "second", "first"], history.Select(h => h.Commit.Subject));
        Assert.Equal((ChangeKind.Renamed, "renamed.txt", "a.txt"), (history[1].Change.Kind, history[1].Change.Path, history[1].Change.OldPath));
        Assert.Equal(("a.txt", ChangeKind.Added), (history[3].Change.Path, history[3].Change.Kind));

        File.WriteAllText(Path.Combine(_repo, "renamed.txt"), "one\ntwo\nthree\nfour\n");
        var blame = await repo.BlameAsync("renamed.txt", null);
        Assert.Equal(["one", "two", "three", "four"], blame.Lines.Select(l => l.Text));
        Assert.Equal([1, 2, 3, 4], blame.Lines.Select(l => l.Number));
        string Subject(int line) => blame.Commits[blame.Lines[line].Hash].Summary;
        Assert.Equal(("first", "second", "add three"), (Subject(0), Subject(1), Subject(2)));
        Assert.False(blame.Commits[blame.Lines[3].Hash].IsCommitted);
        Assert.Equal("t@example.com", blame.Commits[blame.Lines[0].Hash].AuthorEmail);

        // At an older commit, the file is blamed as it was then.
        Assert.Equal(2, (await repo.BlameAsync("a.txt", history[2].Commit.Hash)).Lines.Count);
    }

    [Fact]
    public async Task Revert_and_reset_move_history_back()
    {
        var repo = new GitRepository(_repo);
        Commit("c.txt", "c\n", "add c");
        var withC = (await repo.GetHeadAsync()).Hash!;
        await repo.RevertAsync(withC, isMerge: false, noCommit: false);
        Assert.False(File.Exists(Path.Combine(_repo, "c.txt")));
        Assert.StartsWith("Revert \"add c\"", await repo.GetLastCommitMessageAsync());

        // Hard drops the revert again; then soft keeps the undone commit's changes staged, mixed unstaged, hard drops them.
        await repo.ResetAsync(withC, ResetMode.Hard);
        Assert.True(File.Exists(Path.Combine(_repo, "c.txt")));
        await repo.ResetAsync(withC + "~1", ResetMode.Soft);
        Assert.Equal(["c.txt"], (await repo.GetWorkingChangesAsync()).Staged.Select(c => c.Path));
        await repo.ResetAsync("HEAD", ResetMode.Mixed);
        Assert.Equal(["c.txt"], (await repo.GetWorkingChangesAsync()).Unstaged.Select(c => c.Path));
        Git("add", "c.txt");
        await repo.ResetAsync("HEAD", ResetMode.Hard);
        Assert.Equal(0, (await repo.GetWorkingChangesAsync()).Count);
    }

    [Fact]
    public async Task Rebase_stops_on_conflicts_then_continues_or_aborts()
    {
        var repo = new GitRepository(_repo);
        Git("checkout", "-q", "-b", "topic");
        Commit("a.txt", "topic\n", "topic edit");
        Git("checkout", "-q", "main");
        Commit("a.txt", "main\n", "main edit");
        Git("checkout", "-q", "topic");

        await Assert.ThrowsAsync<GitException>(() => repo.RebaseAsync("main", autostash: false));
        Assert.Equal(GitOperation.Rebase, await repo.GetOperationInProgressAsync());
        Assert.Equal(["a.txt"], (await repo.GetWorkingChangesAsync()).Conflicted.Select(c => c.Path));

        // While rebasing, "theirs" is the commit being replayed: topic's version.
        await repo.TakeSideAsync(["a.txt"], ours: false);
        await repo.ContinueAsync(GitOperation.Rebase);
        Assert.Null(await repo.GetOperationInProgressAsync());
        Assert.Equal("topic\n", File.ReadAllText(Path.Combine(_repo, "a.txt")));
        Assert.Equal("main edit", (await GitRunner.RunAsync(_repo, ["log", "-1", "--format=%s", "HEAD~1"])).Trim());

        // A revert that conflicts can be abandoned.
        var mainEdit = (await GitRunner.RunAsync(_repo, ["rev-parse", "HEAD~1"])).Trim();
        await Assert.ThrowsAsync<GitException>(() => repo.RevertAsync(mainEdit, isMerge: false, noCommit: false));
        Assert.Equal(GitOperation.Revert, await repo.GetOperationInProgressAsync());
        await repo.AbortAsync(GitOperation.Revert);
        Assert.Null(await repo.GetOperationInProgressAsync());
    }

    [Fact]
    public async Task Commits_filter_by_author_date_and_path()
    {
        var repo = new GitRepository(_repo);
        await GitRunner.RunAsync(_repo, ["-c", "user.name=Other Person", "-c", "user.email=other@example.com", "commit", "-q", "--allow-empty", "-m", "by other"]);

        var byOther = await repo.GetCommitsAsync(100, null, true, new CommitFilter("other PERSON", null, null));
        Assert.Equal(["by other"], byOther.Select(c => c.Subject));
        Assert.Empty(await repo.GetCommitsAsync(100, null, true, new CommitFilter(null, DateTimeOffset.Now.AddDays(1), null)));

        // Only commits touching b.txt; their parents are rewritten to the nearest shown commit (none for the first).
        var touchingB = await repo.GetCommitsAsync(100, null, true, new CommitFilter(null, null, "b.txt"));
        Assert.Equal(["feature work"], touchingB.Select(c => c.Subject));
        Assert.Empty(touchingB[0].Parents);
        var touchingA = await repo.GetCommitsAsync(100, null, true, new CommitFilter(null, null, "a.txt"));
        Assert.Equal(["second", "first"], touchingA.Select(c => c.Subject));
        Assert.Equal([touchingA[1].Hash], touchingA[0].Parents);
    }

    [Fact]
    public async Task File_bytes_come_back_unchanged()
    {
        var repo = new GitRepository(_repo);
        byte[] before = [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0x0A, 0x0D], after = [.. before, 0x01];
        File.WriteAllBytes(Path.Combine(_repo, "pic.png"), before);
        Git("add", "pic.png");
        Git("commit", "-q", "-m", "add picture");
        var head = (await repo.GetHeadAsync()).Hash!;
        File.WriteAllBytes(Path.Combine(_repo, "pic.png"), after);

        var change = new FileChange(ChangeKind.Modified, "pic.png", null, null, null);
        var (old, now) = await repo.GetFileBytesAsync(new DiffTarget(head, null), change);
        Assert.Equal(before, old);
        Assert.Equal(after, now);
        var (none, added) = await repo.GetFileBytesAsync(new DiffTarget(head + "~1", head), change with { Kind = ChangeKind.Added });
        Assert.Null(none);
        Assert.Equal(before, added);
    }

    [Fact]
    public async Task Branches_rename_track_and_leave_the_remote_and_tags_get_pushed()
    {
        var remote = Path.Combine(_root, "remote.git");
        Git("init", "-q", "--bare", "-b", "main", remote);
        Git("remote", "add", "origin", remote);
        var repo = new GitRepository(_repo);
        await GitRunner.RunAsync(_repo, ["push", "-q", "origin", "main", "feature"]);
        await repo.FetchAsync();

        await repo.RenameBranchAsync("feature", "topic");
        await repo.SetUpstreamAsync("topic", "origin/feature");
        var refs = await repo.GetRefsAsync();
        Assert.Equal("origin/feature", refs.Single(r => r.Name == "topic").Upstream);

        await repo.DeleteRemoteBranchAsync("origin", "feature");
        Assert.DoesNotContain("feature", await GitRunner.RunAsync(remote, ["branch", "--list"]));

        await repo.CreateTagAsync("v1", "HEAD", null);
        await repo.PushTagAsync("origin", "v1");
        Assert.Equal("v1", (await GitRunner.RunAsync(remote, ["tag", "--list"])).Trim());
    }

    [Fact]
    public async Task Working_tree_changes_include_untracked_files()
    {
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "changed\n");
        File.WriteAllText(Path.Combine(_repo, "new.txt"), "hello\n");
        var repo = new GitRepository(_repo);

        Assert.Equal(2, await repo.CountUncommittedAsync());
        var changes = await repo.GetChangesAsync(new DiffTarget("HEAD", null));
        Assert.Contains(changes, c => c is { Path: "a.txt", Kind: ChangeKind.Modified });
        var untracked = Assert.Single(changes, c => c.Kind == ChangeKind.Untracked);
        var (oldText, newText) = await repo.GetFileVersionsAsync(new DiffTarget("HEAD", null), untracked);
        Assert.Equal(("", "hello\n"), (oldText, newText));
    }

    [Fact]
    public async Task Worktrees_are_added_listed_and_grouped_under_one_repo()
    {
        var repo = new GitRepository(_repo);
        var wtPath = Path.Combine(_root, "app-hotfix");
        await repo.AddWorktreeAsync(wtPath, "main", "hotfix", detach: false);

        var wts = await repo.GetWorktreesAsync();
        Assert.Equal(2, wts.Count);
        Assert.Equal("hotfix", wts[1].BranchShortName);

        var discovered = await RepoDiscovery.DiscoverAsync([_root]);
        var found = Assert.Single(discovered);
        Assert.Equal("app", found.Name);
        Assert.Equal(2, found.Worktrees.Count);

        await repo.RemoveWorktreeAsync(wtPath, force: false);
        Assert.Single(await repo.GetWorktreesAsync());
    }

    [Fact]
    public async Task Empty_repository_has_no_commits()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        await GitRunner.RunAsync(empty, ["init", "-q"]);

        Assert.Empty(await new GitRepository(empty).GetCommitsAsync(10, null, true));
    }
}
