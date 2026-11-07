using GitGraph.App.ViewModels;
using GitGraph.Core.Git;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.Tests;

/// <summary>Commits selected together with Shift+click, cherry-picked or reverted at once.</summary>
public sealed class CommitRangeTests
{
    // On the UI thread git must be awaited: TempRepo.Git blocks it, and git's continuations wait for it (a deadlock).
    private static Task<string> Git(TempRepo repo, params string[] args) => GitRunner.RunAsync(repo.Path, args);

    [Fact]
    public async Task A_range_survives_a_refresh_cherry_picks_oldest_first_and_a_click_ends_it()
    {
        using var temp = new TempRepo();
        temp.Git("checkout", "-q", "-b", "topic");
        foreach (var n in new[] { "x", "y", "z" })
        {
            temp.Write($"{n}.txt", $"{n}\n");
            temp.Git("add", ".");
            temp.Git("commit", "-q", "-m", $"add {n}");
        }
        temp.Git("checkout", "-q", "main");
        temp.Write("main.txt", "main\n");
        temp.Git("add", ".");
        temp.Git("commit", "-q", "-m", "main work");

        await TerminalTestApp.Ui.Dispatch<bool>(async () =>
        {
            var host = new FakeHost();
            using var workspace = new WorkspaceViewModel(new WorkspaceConfig { Name = "Range test" }, host);
            var tab = new RepoTabViewModel(temp.Path, workspace, host);
            await tab.RefreshAsync();
            CommitRowViewModel Row(string subject) => tab.Rows.Single(r => r.Subject == subject);

            tab.SelectedRow = Row("add z");
            tab.SelectRange(Row("add x"));
            Assert.Equal(["add z", "add y", "add x"], tab.Range.Select(r => r.Subject));
            Assert.True(Row("add y").CanApplyRange);
            Assert.False(Row("main work").CanApplyRange);
            Assert.Equal("Cherry-pick 3 selected commits onto current branch…", Row("add x").CherryPickRangeHeader);

            // A refresh rebuilds the rows; the range stays on the same commits.
            temp.Write("untracked.txt", "u\n");
            await tab.RefreshAsync();
            Assert.Equal(3, tab.RangeCount);
            Assert.True(Row("add y").IsInRange);

            await Row("add y").CherryPickRangeCommand.ExecuteAsync(null);
            Assert.Empty(host.Errors);
            Assert.Equal("add z\nadd y\nadd x\nmain work", (await Git(temp, "log", "-4", "--format=%s", "main")).Trim());

            // Picking a row by hand ends the range.
            tab.SelectedRow = Row("main work");
            Assert.Equal(0, tab.RangeCount);
            Assert.All(tab.Rows, r => Assert.False(r.IsInRange));
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task A_range_with_a_merge_is_refused_and_one_without_reverts_newest_first()
    {
        using var temp = new TempRepo();
        temp.Git("checkout", "-q", "-b", "topic");
        temp.Write("t.txt", "t\n");
        temp.Git("add", ".");
        temp.Git("commit", "-q", "-m", "topic work");
        temp.Git("checkout", "-q", "main");
        temp.Git("merge", "-q", "--no-ff", "-m", "merge topic", "topic");
        foreach (var n in new[] { "p", "q" })
        {
            temp.Write($"{n}.txt", $"{n}\n");
            temp.Git("add", ".");
            temp.Git("commit", "-q", "-m", $"add {n}");
        }

        await TerminalTestApp.Ui.Dispatch<bool>(async () =>
        {
            var host = new FakeHost();
            using var workspace = new WorkspaceViewModel(new WorkspaceConfig { Name = "Range test" }, host);
            var tab = new RepoTabViewModel(temp.Path, workspace, host);
            await tab.RefreshAsync();
            CommitRowViewModel Row(string subject) => tab.Rows.Single(r => r.Subject == subject);

            tab.SelectedRow = Row("add q");
            tab.SelectRange(Row("merge topic"));
            await tab.RevertRangeAsync();
            Assert.Contains("is a merge", Assert.Single(host.Errors));

            host.Errors.Clear();
            tab.SelectedRow = Row("add q");
            tab.SelectRange(Row("add p"));
            await tab.RevertRangeAsync();
            Assert.Empty(host.Errors);
            Assert.Equal("Revert \"add p\"\nRevert \"add q\"", (await Git(temp, "log", "-2", "--format=%s")).Trim());
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));
    }
}
