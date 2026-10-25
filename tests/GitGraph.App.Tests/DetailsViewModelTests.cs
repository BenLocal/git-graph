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

    // The loads the last action started, with a limit so a stuck one fails the test instead of hanging it.
    private Task Settled() => _vm.Loads.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));

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
        await Settled();
        Assert.Equal(["Staged changes", "Changes"], _vm.Groups.Select(g => g.Title));
        Assert.Equal(["a.txt"], Paths(_vm, FileGroup.Staged));
        Assert.Equal(["b.txt", "new.txt"], Paths(_vm, FileGroup.Unstaged));
        Assert.Equal(1, _vm.StagedCount);

        // A refresh that finds the same changes must not rebuild the list (that made it flicker).
        var rebuilt = 0;
        _vm.Files.CollectionChanged += (_, _) => rebuilt++;
        _vm.ShowUncommitted("HEAD");
        await Settled();
        Assert.Equal(0, rebuilt);
    }

    [Fact]
    public async Task Selected_files_stage_together_and_the_selection_follows_the_file()
    {
        _repo.Write("x.txt", "x\n");
        _repo.Write("y.txt", "y\n");
        _repo.Write("b.txt", "bee, changed\n");
        _vm.ShowUncommitted("HEAD");
        await Settled();

        FileChangeViewModel File(string path) => _vm.Files.Single(f => f.Change.Path == path);
        _vm.SelectWorkingFile(File("x.txt"), toggle: false, range: false);
        _vm.SelectWorkingFile(File("y.txt"), toggle: false, range: true);
        Assert.Equal(["x.txt", "y.txt"], _vm.Files.Where(f => f.IsSelected).Select(f => f.Change.Path).Order());

        await _vm.StageCommand.ExecuteAsync(File("x.txt"));
        await Settled();
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
        await Settled();
        Assert.NotNull(_vm.DiffLines.First(l => l.IsHunk).Hunk);

        _host.IgnoreWhitespace = true;
        _vm.DiffOptionsChanged();
        await Settled();
        // Only "three" was added; the re-indented line counts as unchanged.
        Assert.Single(_vm.DiffLines, l => l.IsAdded);
    }

    [Fact]
    public async Task Committing_with_nothing_staged_asks_then_stages_everything()
    {
        _repo.Write("b.txt", "bee, changed\n");
        _repo.Write("new.txt", "new\n");
        _vm.ShowUncommitted("HEAD");
        await Settled();

        _vm.CommitMessage = "Everything at once";
        await _vm.CommitCommand.ExecuteAsync(null);

        Assert.Equal(["Commit all changes"], _host.Confirmations);
        Assert.Empty(_host.Errors);
        Assert.Equal("Everything at once", _repo.Git("log", "-1", "--format=%s").Trim());
        Assert.Empty(_repo.Git("status", "--porcelain"));
        Assert.Equal("", _vm.CommitMessage);
    }

    [Fact]
    public async Task Comments_show_under_their_lines_and_follow_edits_and_deletes()
    {
        _repo.Write("a.txt", "one\nTWO\n");
        _repo.Git("commit", "-qam", "second");
        var commit = (await new GitRepository(_repo.Path).GetCommitsAsync(1, null, false)).Single();
        var counts = new List<int>();
        _vm.CommentsChanged = (_, n) => counts.Add(n);

        _vm.ShowCommit(commit);
        await Settled();
        var deleted = _vm.DiffLines.Single(l => l.IsDeleted);
        var added = _vm.DiffLines.Single(l => l.IsAdded);
        Assert.Equal((true, 2), (deleted.Spot!.OldSide, deleted.Spot.Line));
        Assert.Equal((false, 2), (added.Spot!.OldSide, added.Spot.Line));
        Assert.Null(_vm.DiffLines.Single(l => l.IsHunk).Spot);

        _host.PromptAnswers.Enqueue(["Why upper case?"]);
        await added.Spot.AddCommand.ExecuteAsync(null);
        await Settled();
        var comment = Assert.Single(_vm.DiffLines.Single(l => l.IsAdded).Comments);
        Assert.Equal(("Why upper case?", "Tester", true), (comment.Text, comment.Author, comment.IsMine));
        Assert.Empty(_vm.DiffLines.Single(l => l.IsDeleted).Comments);
        // The split view shows it under the row of both sides.
        Assert.Single(_vm.SplitRows.Single(r => r.Right.IsAdded).Comments);

        _host.PromptAnswers.Enqueue(["Why upper case here?"]);
        await comment.EditCommand.ExecuteAsync(null);
        await Settled();
        comment = Assert.Single(_vm.DiffLines.Single(l => l.IsAdded).Comments);
        Assert.Equal("Why upper case here?", comment.Text);

        await comment.DeleteCommand.ExecuteAsync(null);
        await Settled();
        Assert.Empty(_vm.DiffLines.Single(l => l.IsAdded).Comments);
        Assert.Equal([0, 1, 1, 0], counts);

        // Uncommitted changes can't take comments.
        _repo.Write("b.txt", "changed\n");
        _vm.ShowUncommitted("HEAD");
        await Settled();
        Assert.All(_vm.DiffLines, l => Assert.Null(l.Spot));
    }

    [Fact]
    public async Task The_full_diff_says_who_last_changed_the_caret_line_on_each_side()
    {
        _repo.Write("a.txt", "one\nTWO\n");
        _repo.Git("-c", "user.name=Ana", "commit", "-qam", "Shout two");
        var commit = (await new GitRepository(_repo.Path).GetCommitsAsync(1, null, false)).Single();
        _vm.ShowCommit(commit);
        await Settled();
        await _vm.OpenFullDiffCommand.ExecuteAsync(null);
        var diff = Assert.Single(_host.Diffs);

        // The blame arrives after the window opens; the note follows the caret from then on.
        var shown = new TaskCompletionSource();
        diff.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FullDiffViewModel.LineBlame) && diff.LineBlame != null) shown.TrySetResult(); };
        diff.SetCaret(false, 1);
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new LineNote(false, 1, "Ana, just now • Shout two"), diff.LineBlame);
        // The old side is blamed at the parent: line 2 was written by the first commit.
        diff.SetCaret(true, 1);
        Assert.Equal("Tester, just now • first", diff.LineBlame?.Text);
        diff.Detach();
    }
}
