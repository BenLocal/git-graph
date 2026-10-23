using GitGraph.App.Services;
using GitGraph.App.ViewModels;
using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.App.Tests;

/// <summary>The full diff window: stepping through changes, the caret's line details, and folds.</summary>
public sealed class FullDiffViewModelTests
{
    // 30 lines; line 5 changes, line 25 is removed and a line is added at the end.
    private static FullDiffViewModel Build()
    {
        var old = Enumerable.Range(1, 30).Select(i => $"line {i}").ToList();
        var @new = old.ToList();
        @new[4] = "line five";
        @new.RemoveAt(24);
        @new.Add("added");
        var diff = TextDiff.Build(string.Join('\n', old) + "\n", string.Join('\n', @new) + "\n");
        return new FullDiffViewModel(new FileChange(ChangeKind.Modified, "a.txt", null, 2, 2), "a → b", diff, DiffSyntax.Empty, null, new FakeHost(), "/a.txt");
    }

    [Fact]
    public void Next_and_previous_step_through_the_changes_and_wrap()
    {
        var vm = Build();
        var revealed = new List<DiffBlock>();
        vm.RevealRequested += revealed.Add;

        Assert.Equal(3, vm.Changes.Count);
        Assert.Equal("3 changes", vm.ChangePosition);
        vm.NextChangeCommand.Execute(null);
        vm.NextChangeCommand.Execute(null);
        Assert.Equal("2 of 3", vm.ChangePosition);
        Assert.Equal(24, revealed[^1].OldStart);
        vm.NextChangeCommand.Execute(null);
        vm.NextChangeCommand.Execute(null);
        Assert.Equal(0, vm.CurrentChange);
        vm.PreviousChangeCommand.Execute(null);
        Assert.Equal(2, vm.CurrentChange);
        Assert.Equal((0, 1), (revealed[^1].OldCount, revealed[^1].NewCount));
    }

    [Fact]
    public void The_caret_shows_its_line_and_the_counterpart_and_selects_the_change()
    {
        var vm = Build();

        vm.SetCaret(old: false, line: 4);
        Assert.Equal(("line 5", "line five"), (vm.DetailOld!.Text, vm.DetailNew!.Text));
        Assert.Equal(("5", "5"), (vm.DetailOldNo, vm.DetailNewNo));
        Assert.Equal(0, vm.CurrentChange);

        // The removed line has no counterpart.
        vm.SetCaret(old: true, line: 24);
        Assert.Equal("line 25", vm.DetailOld!.Text);
        Assert.Null(vm.DetailNew);
        Assert.True(vm.IsDetailDeleted);
        Assert.Equal(1, vm.CurrentChange);

        // Unchanged lines pair up across the removal, and keep the current change.
        vm.SetCaret(old: true, line: 27);
        Assert.Equal(("28", "27"), (vm.DetailOldNo, vm.DetailNewNo));
        Assert.Equal(1, vm.CurrentChange);
    }

    [Fact]
    public void Unchanged_stretches_fold_at_the_same_lines_of_both_sides()
    {
        var vm = Build();

        var fold = Assert.Single(vm.Folds, f => f.OldStart > 5);
        // Lines 9 to 21 sit between the changes' context, at the same place on both sides.
        Assert.Equal((8, 8, 13), (fold.OldStart, fold.NewStart, fold.Count));
        vm.ExpandAllCommand.Execute(null);
        Assert.All(vm.Folds, f => Assert.True(f.IsExpanded));
        vm.CollapseAllCommand.Execute(null);
        Assert.All(vm.Folds, f => Assert.False(f.IsExpanded));
    }
}
