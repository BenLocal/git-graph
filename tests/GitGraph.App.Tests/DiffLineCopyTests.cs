using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;
using GitGraph.Core.Diff;

namespace GitGraph.App.Tests;

/// <summary>Lines of a diff list can be selected and copied, as the diff rows aren't an editor's text.</summary>
public sealed class DiffLineCopyTests
{
    private static HeadlessUnitTestSession Ui => TerminalTestApp.Ui;

    private static readonly DiffLine[] Lines =
    [
        new(DiffLineKind.Hunk, null, null, "@@ -1,3 +1,3 @@"),
        new(DiffLineKind.Context, 1, 1, "first"),
        new(DiffLineKind.Deleted, 2, null, "old second"),
        new(DiffLineKind.Added, null, 2, "new second"),
        new(DiffLineKind.Context, 3, 3, "third"),
    ];

    [Fact]
    public void The_text_leaves_out_hunk_headers_and_the_empty_side()
    {
        var unified = Lines.Select(l => new DiffLineViewModel(l, DiffSyntax.Empty)).ToList();
        Assert.Equal("first\nold second\nnew second", DiffLineCopy.Text(unified.Take(4), left: false));

        var split = new[]
        {
            new SplitRowViewModel(new SplitDiffRow(Lines[0], null, IsHunk: true), DiffSyntax.Empty),
            new SplitRowViewModel(new SplitDiffRow(Lines[1], Lines[1], false), DiffSyntax.Empty),
            new SplitRowViewModel(new SplitDiffRow(Lines[2], null, false), DiffSyntax.Empty),
        };
        Assert.Equal("first\nold second", DiffLineCopy.Text(split, left: true));
        Assert.Equal("first", DiffLineCopy.Text(split, left: false));
    }

    /// <summary>Shows the rows in a diff list, runs <paramref name="act"/>, and returns what is on the clipboard.</summary>
    private static Task<string?> RunAsync(object[] rows, string template, Action<Window, ListBox> act) => Ui.Dispatch(async () =>
    {
        var list = new ListBox
        {
            ItemsSource = rows,
            ItemTemplate = (Avalonia.Controls.Templates.IDataTemplate)Application.Current!.FindResource(template)!,
        };
        list.Classes.Add("dense");
        list.Classes.Add("plain");
        DiffLineCopy.SetIsEnabled(list, true);
        var window = new Window { Width = 900, Height = 400, Content = list };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            await window.Clipboard!.SetTextAsync("");
            act(window, list);
            Dispatcher.UIThread.RunJobs();
            return await window.Clipboard!.TryGetTextAsync();
        }
        finally { window.Close(); }
    }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

    private static Point On(Window window, Func<Control, bool> pick, double x = 0.5)
    {
        var target = window.GetVisualDescendants().OfType<Control>().First(pick);
        return target.TranslatePoint(new Point(target.Bounds.Width * x, target.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Not in the window.");
    }

    private static void Click(Window window, Point at, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(at, MouseButton.Left, modifiers);
        window.MouseUp(at, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressCopy(Window window, ListBox list)
    {
        var copy = list.GetPlatformSettings()!.HotkeyConfiguration.Copy[0];
        window.KeyPress(copy.Key, (RawInputModifiers)copy.KeyModifiers, PhysicalKey.C, "c");
    }

    private static Func<Control, bool> Row(object row) => c => c is ListBoxItem && c.DataContext == row;

    [Fact]
    public async Task A_shift_click_range_copies_its_lines_with_the_shortcut()
    {
        var rows = Lines.Select(l => (object)new DiffLineViewModel(l, DiffSyntax.Empty)).ToArray();

        var copied = await RunAsync(rows, "UnifiedLineTemplate", (window, list) =>
        {
            Click(window, On(window, Row(rows[1]), x: 0.6));
            Click(window, On(window, Row(rows[3]), x: 0.6), RawInputModifiers.Shift);
            PressCopy(window, list);
        });

        Assert.Equal("first\nold second\nnew second", copied);
    }

    [Theory]
    [InlineData(0.25, "first\nold second")]
    [InlineData(0.75, "first\nnew second")]
    public async Task A_side_by_side_row_copies_the_side_that_was_clicked(double x, string expected)
    {
        var rows = new object[]
        {
            new SplitRowViewModel(new SplitDiffRow(Lines[1], Lines[1], false), DiffSyntax.Empty),
            new SplitRowViewModel(new SplitDiffRow(Lines[2], Lines[3], false), DiffSyntax.Empty),
        };

        var copied = await RunAsync(rows, "SplitRowTemplate", (window, list) =>
        {
            Click(window, On(window, Row(rows[0]), x));
            Click(window, On(window, Row(rows[1]), x), RawInputModifiers.Shift);
            PressCopy(window, list);
        });

        Assert.Equal(expected, copied);
    }
}
