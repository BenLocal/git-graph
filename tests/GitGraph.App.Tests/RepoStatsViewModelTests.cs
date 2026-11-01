using GitGraph.App.ViewModels;
using GitGraph.Core.Git;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.Tests;

public sealed class RepoStatsViewModelTests
{
    [Fact]
    public async Task Closing_a_tab_cancels_its_pending_statistics()
    {
        using var temp = new TempRepo();
        temp.Write("app.py", "print('hello')\n");
        temp.Git("add", ".");
        temp.Git("commit", "-q", "-m", "python");
        await TerminalTestApp.Ui.Dispatch<bool>(async () =>
        {
            var host = new FakeHost();
            using var workspace = new WorkspaceViewModel(new WorkspaceConfig { Name = "Stats test" }, host);
            var tab = new RepoTabViewModel(temp.Path, workspace, host);
            workspace.Tabs.Add(tab);
            var pending = tab.Stats.EnsureLoadedAsync("main");

            workspace.CloseTabCommand.Execute(tab);
            await pending.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(tab.Stats.IsLoading);
            Assert.False(tab.Stats.IsCountingLines);
            Assert.Empty(tab.Stats.Languages);
            Assert.Empty(tab.Stats.Contributors);
            Assert.Null(tab.Stats.Summary);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Closing_a_loaded_tab_releases_statistics_and_prevents_reloading()
    {
        using var temp = new TempRepo();
        await TerminalTestApp.Ui.Dispatch<bool>(async () =>
        {
            var host = new FakeHost();
            using var workspace = new WorkspaceViewModel(new WorkspaceConfig { Name = "Stats test" }, host);
            var tab = new RepoTabViewModel(temp.Path, workspace, host);
            workspace.Tabs.Add(tab);
            await tab.Stats.EnsureLoadedAsync("main");
            Assert.Single(tab.Stats.Contributors);

            workspace.CloseTabCommand.Execute(tab);
            tab.Dispose();
            await tab.Stats.EnsureLoadedAsync("main");

            Assert.Empty(tab.Stats.Contributors);
            Assert.Empty(tab.Stats.Weeks);
            Assert.Empty(tab.Stats.WeeklyCommits);
            Assert.Empty(tab.Stats.DailyCommits);
            Assert.Null(tab.Stats.Summary);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Invalidating_during_card_publication_stops_the_old_load()
    {
        using var temp = new TempRepo();
        for (var i = 0; i < 5; i++)
        {
            temp.Write("other.txt", $"contribution {i}\n");
            temp.Git("add", ".");
            temp.Git("-c", $"user.email=other{i}@example.com", "commit", "-q", "-m", $"other {i}");
        }
        await TerminalTestApp.Ui.Dispatch<bool>(async () =>
        {
            using var vm = new RepoStatsViewModel(new GitRepository(temp.Path));
            var invalidated = false;
            vm.Contributors.CollectionChanged += (_, _) =>
            {
                if (invalidated || vm.Contributors.Count != 4) return;
                invalidated = true;
                vm.Invalidate();
                vm.Summary = "Invalidated";
            };
            await vm.EnsureLoadedAsync("main").WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(invalidated);
            Assert.Equal(4, vm.Contributors.Count);
            Assert.Equal("Invalidated", vm.Summary);
            Assert.False(vm.IsLoading);
            Assert.False(vm.IsCountingLines);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Invalidating_a_rebuild_prevents_its_result_from_being_published()
    {
        using var temp = new TempRepo();
        temp.Write("other.txt", "another contributor\n");
        temp.Git("add", ".");
        temp.Git("-c", "user.name=Other", "-c", "user.email=other@example.com", "commit", "-q", "-m", "other");
        await TerminalTestApp.Ui.Dispatch<bool>(async () =>
        {
            using var vm = new RepoStatsViewModel(new GitRepository(temp.Path));
            await vm.EnsureLoadedAsync("main");
            var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.Sort = new SortOption("Invalidate during ranking", person =>
            {
                vm.Invalidate();
                vm.Summary = "Invalidated";
                invalidated.TrySetResult();
                return person.Commits;
            });
            await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Invalidated", vm.Summary);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Languages_refresh_after_head_changes_and_ignore_the_contribution_period()
    {
        using var temp = new TempRepo();
        using var vm = new RepoStatsViewModel(new GitRepository(temp.Path));
        await vm.EnsureLoadedAsync("main");
        Assert.Empty(vm.Languages);
        Assert.Equal("No recognized source files in this commit.", vm.LanguageMessage);

        temp.Write("app.py", "print('hello')\n");
        temp.Git("add", ".");
        temp.Git("commit", "-q", "-m", "python");
        vm.Invalidate();
        await vm.EnsureLoadedAsync("main");
        Assert.Equal("Python", Assert.Single(vm.Languages).Name);
        Assert.Equal(1, vm.Languages[0].Share);
        vm.Period = vm.Periods[^1];
        Assert.Equal("Python", Assert.Single(vm.Languages).Name);

        temp.Git("checkout", "-q", "--detach", "HEAD~1");
        vm.Invalidate();
        Assert.Empty(vm.Languages);
        await vm.EnsureLoadedAsync(null);
        Assert.Empty(vm.Languages);
    }
}
