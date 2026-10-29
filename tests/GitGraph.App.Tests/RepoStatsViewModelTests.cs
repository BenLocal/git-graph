using GitGraph.App.ViewModels;
using GitGraph.Core.Git;

namespace GitGraph.App.Tests;

public sealed class RepoStatsViewModelTests
{
    [Fact]
    public async Task Languages_refresh_after_head_changes_and_ignore_the_contribution_period()
    {
        using var temp = new TempRepo();
        var vm = new RepoStatsViewModel(new GitRepository(temp.Path));
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
