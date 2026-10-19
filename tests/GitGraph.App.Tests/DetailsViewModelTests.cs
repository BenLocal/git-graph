using GitGraph.App.ViewModels;
using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.App.Tests;

/// <summary>The uncommitted changes view: grouping, selection, staging and committing, against a real repository.</summary>
public sealed class DetailsViewModelTests : IDisposable
{
    private readonly TempRepo _repo = new();
    private readonly FakeHost _host = new();
    private readonly DetailsViewModel _vm;

    public DetailsViewModelTests()
    {
        _vm = new DetailsViewModel(new GitRepository(_repo.Path), _host);
        // As the repository tab does after every change: reload the uncommitted changes.
        _vm.RepoChanged = () =>
        {
            _vm.ShowUncommitted("HEAD");
            return Task.CompletedTask;
        };
    }

    public void Dispose() => _repo.Dispose();

    private static string[] Paths(DetailsViewModel vm, FileGroup group) =>
        vm.Files.Where(f => f.Group == group).Select(f => f.Change.Path).Order().ToArray();

    [Fact]
    public async Task Uncommitted_changes_are_grouped_and_an_unchanged_refresh_keeps_the_list()
    {
        _repo.Write("a.txt", "one\ntwo\nthree\n");
        _repo.Git("add", "a.txt");
        _repo.Write("b.txt", "bee, changed\n");
        _repo.Write("new.txt", "new\n");

        _vm.ShowUncommitted("HEAD");
        await Wait.Until(() => _vm.Groups.Count == 2, "the staged and unstaged groups");
        Assert.Equal(["Staged changes", "Changes"], _vm.Groups.Select(g => g.Title));
        Assert.Equal(["a.txt"], Paths(_vm, FileGroup.Staged));
        Assert.Equal(["b.txt", "new.txt"], Paths(_vm, FileGroup.Unstaged));
        Assert.Equal(1, _vm.StagedCount);

        // A refresh that finds the same changes must not rebuild the list (that made it flicker).
        var rebuilt = 0;
        _vm.Files.CollectionChanged += (_, _) => rebuilt++;
        _vm.ShowUncommitted("HEAD");
        await Task.Delay(500);
        Assert.Equal(0, rebuilt);
    }

    [Fact]
    public async Task Selected_files_stage_together_and_the_selection_follows_the_file()
    {
        _repo.Write("x.txt", "x\n");
        _repo.Write("y.txt", "y\n");
        _repo.Write("b.txt", "bee, changed\n");
        _vm.ShowUncommitted("HEAD");
        await Wait.Until(() => _vm.Files.Count == 3, "three changed files");

        FileChangeViewModel File(string path) => _vm.Files.Single(f => f.Change.Path == path);
        _vm.SelectWorkingFile(File("x.txt"), toggle: false, range: false);
        _vm.SelectWorkingFile(File("y.txt"), toggle: false, range: true);
        Assert.Equal(["x.txt", "y.txt"], _vm.Files.Where(f => f.IsSelected).Select(f => f.Change.Path).Order());

        await _vm.StageCommand.ExecuteAsync(File("x.txt"));
        await Wait.Until(() => Paths(_vm, FileGroup.Staged).Length == 2, "both selected files staged");
        Assert.Equal(["x.txt", "y.txt"], Paths(_vm, FileGroup.Staged));
        Assert.Equal(["b.txt"], Paths(_vm, FileGroup.Unstaged));
        // The diff stays on the file last picked, now in its staged group.
        Assert.Equal((FileGroup.Staged, "y.txt"), (_vm.SelectedFile!.Group, _vm.SelectedFile.Change.Path));
    }

    [Fact]
    public async Task Hunk_buttons_are_hidden_while_whitespace_is_ignored()
    {
        _repo.Write("a.txt", "one\n  two\nthree\n");
        _vm.ShowUncommitted("HEAD");
        await Wait.Until(() => _vm.DiffLines.Any(l => l.IsHunk), "the diff of a.txt");
        Assert.NotNull(_vm.DiffLines.First(l => l.IsHunk).Hunk);

        _host.IgnoreWhitespace = true;
        _vm.DiffOptionsChanged();
        await Wait.Until(() => _vm.DiffLines.Any(l => l.IsHunk) && _vm.DiffLines.First(l => l.IsHunk).Hunk is null, "a diff without hunk buttons");
        // Only "three" was added; the re-indented line counts as unchanged.
        Assert.Single(_vm.DiffLines, l => l.IsAdded);
    }

    [Fact]
    public async Task Committing_with_nothing_staged_asks_then_stages_everything()
    {
        _repo.Write("b.txt", "bee, changed\n");
        _repo.Write("new.txt", "new\n");
        _vm.ShowUncommitted("HEAD");
        await Wait.Until(() => _vm.Files.Count == 2, "two changed files");

        _vm.CommitMessage = "Everything at once";
        await _vm.CommitCommand.ExecuteAsync(null);

        Assert.Equal(["Commit all changes"], _host.Confirmations);
        Assert.Empty(_host.Errors);
        Assert.Equal("Everything at once", _repo.Git("log", "-1", "--format=%s").Trim());
        Assert.Empty(_repo.Git("status", "--porcelain"));
        Assert.Equal("", _vm.CommitMessage);
    }
}
