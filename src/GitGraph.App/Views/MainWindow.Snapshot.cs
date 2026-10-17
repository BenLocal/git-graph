using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>
/// Visual check hook: with GITGRAPH_SNAPSHOT=out.png the window renders itself to a file once loaded.
/// GITGRAPH_SNAPSHOT_SELECT=n selects the n-th row first, GITGRAPH_SNAPSHOT_COMPARE=m compares it with row m,
/// GITGRAPH_SNAPSHOT_TAB=0|1 picks the Commit or Changes tab of the details pane,
/// GITGRAPH_SNAPSHOT_PANEL=max|hidden maximizes or hides that pane, GITGRAPH_SNAPSHOT_SIDEBAR=0 hides the sidebar,
/// and GITGRAPH_SNAPSHOT_FULLDIFF=1 opens the selected file's full-screen diff and renders that window instead
/// (=expanded also opens every fold in it).
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
                vm.SelectedWorkspace.SelectedTab is { } tab && index < tab.Rows.Count)
            {
                tab.SelectedRow = tab.Rows[index];
                if (int.TryParse(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_COMPARE"), out var other) && other < tab.Rows.Count)
                    tab.SetCompare(tab.Rows[other]);
                if (int.TryParse(Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_TAB"), out var details))
                    tab.Details.SelectedTabIndex = details;
                switch (Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_PANEL"))
                {
                    case "max": tab.Details.IsMaximized = true; break;
                    case "hidden": tab.Details.IsPanelVisible = false; break;
                }
                if (Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_SIDEBAR") == "0") vm.ShowSidebar = false;
                await Task.Delay(1500);
            }
            Window target = this;
            var fullDiff = Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_FULLDIFF");
            if (fullDiff is "1" or "expanded" &&
                DataContext is MainWindowViewModel main && main.SelectedWorkspace.SelectedTab is { } current)
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
            var size = new PixelSize((int)target.Bounds.Width, (int)target.Bounds.Height);
            using var bmp = new RenderTargetBitmap(size);
            bmp.Render(target);
            bmp.Save(path);
            Close();
        }, TimeSpan.FromSeconds(3));
    }
}
