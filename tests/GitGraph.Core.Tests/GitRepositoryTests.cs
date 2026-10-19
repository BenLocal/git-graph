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
