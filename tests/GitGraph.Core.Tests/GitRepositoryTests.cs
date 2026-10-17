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
