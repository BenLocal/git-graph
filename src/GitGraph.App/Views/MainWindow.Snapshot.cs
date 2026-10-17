using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>
/// Visual check hook: with GITGRAPH_SNAPSHOT=out.png the window renders itself to a file once loaded.
/// GITGRAPH_SNAPSHOT_SELECT=n selects the n-th row first; GITGRAPH_SNAPSHOT_THEME=Light|Dark overrides the theme.
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
                await Task.Delay(1500);
            }
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var bmp = new RenderTargetBitmap(size);
            bmp.Render(this);
            bmp.Save(path);
            Close();
        }, TimeSpan.FromSeconds(3));
    }
}
