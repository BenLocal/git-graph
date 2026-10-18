using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>
/// Visual check hook: with GITGRAPH_SNAPSHOT=out.png the window renders itself to a file once loaded.
/// GITGRAPH_SNAPSHOT_SELECT=n selects the n-th row first, GITGRAPH_SNAPSHOT_COMPARE=m compares it with row m,
/// GITGRAPH_SNAPSHOT_TAB=0|1 picks the Commit or Changes tab of the details pane,
/// GITGRAPH_SNAPSHOT_FILE=n selects the n-th changed file, GITGRAPH_SNAPSHOT_PANEL=max|hidden maximizes or hides that pane, GITGRAPH_SNAPSHOT_SIDEBAR=0 hides the sidebar,
/// and GITGRAPH_SNAPSHOT_FULLDIFF=1 opens the selected file's full-screen diff and renders that window instead
/// (=expanded also opens every fold in it). GITGRAPH_SNAPSHOT_SCROLL=n scrolls the commit list to the end n times
/// first, to exercise infinite scrolling; the row count is printed. GITGRAPH_SNAPSHOT_SHORTCUTS=1 renders the
/// Keyboard Shortcuts window instead (any other value searches it). GITGRAPH_SNAPSHOT_STATS=1 shows Contributors. GITGRAPH_SNAPSHOT_DELAY=seconds waits longer before rendering. GITGRAPH_SNAPSHOT_ABOUT=1 renders the About window.
/// </summary>
public partial class MainWindow
{
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT") is not { Length: > 0 } path) return;
        DispatcherTimer.RunOnce(async () =>
        {
            if (DataContext is MainWindowViewModel vm &&
                int.TryParse(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_SELECT"), out var index) &&
                vm.SelectedWorkspace.SelectedRepoTab is { } tab && index < tab.Rows.Count)
            {
                tab.SelectedRow = tab.Rows[index];
                if (int.TryParse(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_COMPARE"), out var other) && other < tab.Rows.Count)
                    tab.SetCompare(tab.Rows[other]);
                if (Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_STATS") == "1")
                {
                    tab.ShowStats = true;
                    await Task.Delay(1500);
                }
                if (int.TryParse(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_TAB"), out var details))
                    tab.Details.SelectedTabIndex = details;
                switch (Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_PANEL"))
                {
                    case "max": tab.Details.IsMaximized = true; break;
                    case "hidden": tab.Details.IsPanelVisible = false; break;
                }
                if (Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_SIDEBAR") == "0") vm.ShowSidebar = false;
                await Task.Delay(1500);
                if (int.TryParse(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_FILE"), out var file) && file < tab.Details.Files.Count)
                {
                    tab.Details.SelectedFile = tab.Details.Files[file];
                    await Task.Delay(1000);
                }
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_SCROLL"), out var scrolls)
                && this.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(l => l.Name == "CommitList") is { Scroll: ScrollViewer list })
            {
                for (var i = 0; i < scrolls; i++)
                {
                    list.ScrollToEnd();
                    await Task.Delay(1500);
                }
                list.Offset = list.Offset.WithY(list.Offset.Y - 200);
                await Task.Delay(500);
                if (DataContext is MainWindowViewModel m && m.SelectedWorkspace.SelectedRepoTab is { } t)
                    Console.WriteLine($"snapshot: {t.Rows.Count} rows, offset {list.Offset.Y:0}, extent {list.Extent.Height:0}");
            }
            Window target = this;
            var fullDiff = Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_FULLDIFF");
            if (fullDiff is "1" or "expanded" &&
                DataContext is MainWindowViewModel main && main.SelectedWorkspace.SelectedRepoTab is { } current)
            {
                await current.Details.OpenFullDiffCommand.ExecuteAsync(null);
                await Task.Delay(1000);
                target = OwnedWindows.LastOrDefault() ?? this;
                if (fullDiff == "expanded" && target.DataContext is FullDiffViewModel diff)
                {
                    diff.ExpandAllCommand.Execute(null);
                    await Task.Delay(500);
                }
            }
            if (Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_ABOUT") == "1")
            {
                _ = new AboutWindow().ShowDialog(this);
                await Task.Delay(800);
                target = OwnedWindows.LastOrDefault() ?? this;
            }
            if (Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_SHORTCUTS") is { Length: > 0 } filter)
            {
                RunShortcut("KeyboardShortcuts");
                await Task.Delay(1000);
                target = OwnedWindows.LastOrDefault() ?? this;
                if (filter != "1" && target.DataContext is ShortcutsEditorViewModel editor) editor.Filter = filter;
                await Task.Delay(300);
            }
            // 1x by default; GITGRAPH_SNAPSHOT_SCALE=2 renders at a HiDPI scale to check sharpness.
            var scale = double.TryParse(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_SCALE"), out var forced) ? forced : 1;
            var size = new PixelSize((int)(target.Bounds.Width * scale), (int)(target.Bounds.Height * scale));
            using var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bmp.Render(target);
            bmp.Save(path, PngBitmapEncoderOptions.Default);
            Close();
        }, TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_DELAY"), out var wait) ? wait : 3));
    }
}
