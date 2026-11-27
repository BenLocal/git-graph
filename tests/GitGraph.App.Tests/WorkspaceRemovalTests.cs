using GitGraph.App.ViewModels;
using GitGraph.Core.Git;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.Tests;

public class WorkspaceRemovalTests
{
    [Fact]
    public async Task Removing_directly_added_repo_closes_tab_removes_folder_and_stays_gone_after_rescan()
    {
        using var repo = new TempRepo();
        await TerminalTestApp.Ui.Dispatch<bool>(async () =>
        {
            var host = new FakeHost { ConfirmAnswer = true };
            var config = new WorkspaceConfig { Name = "Default", Folders = [repo.Path] };
            using var ws = new WorkspaceViewModel(config, host);

            await ws.ScanAsync();
            Assert.Single(ws.Repos);
            ws.OpenTab(repo.Path, pin: true);
            Assert.Single(ws.Tabs);

            await ws.Repos[0].RemoveCommand.ExecuteAsync(null);

            Assert.Equal(["Remove repository"], host.Confirmations);
            Assert.Empty(ws.Repos);
            Assert.Empty(ws.Folders);
            Assert.Empty(config.Folders);
            Assert.Empty(config.HiddenRepos);
            Assert.Empty(ws.Tabs);

            await ws.ScanAsync();
            Assert.Empty(ws.Repos);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Removing_repo_under_parent_folder_hides_it_across_rescans_and_readding_restores_it()
    {
        var parent = Path.Combine(Path.GetTempPath(), "gitgraph-parent-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var repoA = Path.Combine(parent, "repo-a");
            var repoB = Path.Combine(parent, "repo-b");
            InitRepo(repoA);
            InitRepo(repoB);

            await TerminalTestApp.Ui.Dispatch<bool>(async () =>
            {
                var host = new FakeHost { ConfirmAnswer = true };
                var config = new WorkspaceConfig { Name = "Default", Folders = [parent] };
                using var ws = new WorkspaceViewModel(config, host);

                await ws.ScanAsync();
                Assert.Equal(2, ws.Repos.Count);

                var target = ws.Repos.Single(r => r.Name == "repo-a");
                await target.RemoveCommand.ExecuteAsync(null);

                Assert.Single(ws.Repos);
                Assert.Equal("repo-b", ws.Repos[0].Name);
                Assert.Single(ws.Folders);
                Assert.Contains(repoA, config.HiddenRepos);

                await ws.ScanAsync();
                Assert.Single(ws.Repos);
                Assert.Equal("repo-b", ws.Repos[0].Name);

                host.PickedFolder = repoA;
                await ws.AddFolderCommand.ExecuteAsync(null);

                Assert.Equal(2, ws.Repos.Count);
                Assert.Empty(config.HiddenRepos);
                return true;
            }, CancellationToken.None);
        }
        finally
        {
            try { Directory.Delete(parent, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Cancelling_removal_leaves_repo_folders_and_tabs_intact()
    {
        using var repo = new TempRepo();
        await TerminalTestApp.Ui.Dispatch<bool>(async () =>
        {
            var host = new FakeHost { ConfirmAnswer = false };
            var config = new WorkspaceConfig { Name = "Default", Folders = [repo.Path] };
            using var ws = new WorkspaceViewModel(config, host);

            await ws.ScanAsync();
            ws.OpenTab(repo.Path, pin: true);

            await ws.Repos[0].RemoveCommand.ExecuteAsync(null);

            Assert.Equal(["Remove repository"], host.Confirmations);
            Assert.Single(ws.Repos);
            Assert.Single(ws.Folders);
            Assert.Single(ws.Tabs);
            return true;
        }, CancellationToken.None);
    }

    private static void InitRepo(string dir)
    {
        Directory.CreateDirectory(dir);
        GitRunner.RunAsync(dir, ["init", "-q", "-b", "main"]).GetAwaiter().GetResult();
        GitRunner.RunAsync(dir, ["config", "user.name", "Tester"]).GetAwaiter().GetResult();
        GitRunner.RunAsync(dir, ["config", "user.email", "t@example.com"]).GetAwaiter().GetResult();
        GitRunner.RunAsync(dir, ["config", "commit.gpgsign", "false"]).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(dir, "README.md"), "hi\n");
        GitRunner.RunAsync(dir, ["add", "."]).GetAwaiter().GetResult();
        GitRunner.RunAsync(dir, ["commit", "-q", "-m", "init"]).GetAwaiter().GetResult();
    }
}
