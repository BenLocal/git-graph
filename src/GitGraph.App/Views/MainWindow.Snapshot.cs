using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>
/// Visual check hook: with GITGRAPH_SNAPSHOT=out.png the window renders itself to a file once loaded.
/// GITGRAPH_SNAPSHOT_SELECT=n selects the n-th row first, GITGRAPH_SNAPSHOT_COMPARE=m compares it with row m,
/// GITGRAPH_SNAPSHOT_TAB=0|1 picks the Commit or Changes tab of the details pane,
/// GITGRAPH_SNAPSHOT_FILE=n selects the n-th changed file, GITGRAPH_SNAPSHOT_PANEL=max|hidden maximizes or hides that pane, GITGRAPH_SNAPSHOT_SIDEBAR=0 hides the sidebar,
/// and GITGRAPH_SNAPSHOT_FULLDIFF=1 opens the selected file's full-screen diff and renders that window instead
/// (=expanded also opens every fold in it); GITGRAPH_SNAPSHOT_HISTORY=history|blame renders the selected file's history window. GITGRAPH_SNAPSHOT_SCROLL=n scrolls the commit list to the end n times
/// first, to exercise infinite scrolling; the row count is printed. GITGRAPH_SNAPSHOT_SHORTCUTS=1 renders the
/// Keyboard Shortcuts window instead (any other value searches it). GITGRAPH_SNAPSHOT_STATS=1 shows Contributors. GITGRAPH_SNAPSHOT_ABOUT=1 renders the About window.
/// GITGRAPH_SNAPSHOT_TERMINAL=1 opens the embedded shell and waits for a command to complete before rendering.
/// Each step waits for the loads it started (<see cref="PendingLoads"/>) and a layout pass, not for a fixed time.
/// </summary>
public partial class MainWindow
{
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT") is not { Length: > 0 } path) return;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await SnapshotAsync(path); }
            catch (Exception ex) { Console.WriteLine($"snapshot failed: {ex}"); }
            Close();
        }, DispatcherPriority.Background);
    }

    private async Task SnapshotAsync(string path)
    {
        static string? Env(string name) => Environment.GetEnvironmentVariable("GITGRAPH_SNAPSHOT_" + name);
        var vm = (MainWindowViewModel)DataContext!;
        await SettledAsync();

        if (int.TryParse(Env("SELECT"), out var index) && vm.SelectedWorkspace.SelectedRepoTab is { } tab && index < tab.Rows.Count)
        {
            tab.SelectedRow = tab.Rows[index];
            if (int.TryParse(Env("COMPARE"), out var other) && other < tab.Rows.Count) tab.SetCompare(tab.Rows[other]);
            if (Env("STATS") == "1") tab.ShowStats = true;
            if (int.TryParse(Env("TAB"), out var details)) tab.Details.SelectedTabIndex = details;
            switch (Env("PANEL"))
            {
                case "max": tab.Details.IsMaximized = true; break;
                case "hidden": tab.Details.IsPanelVisible = false; break;
            }
            if (Env("SIDEBAR") == "0") vm.ShowSidebar = false;
            await SettledAsync();
            if (int.TryParse(Env("FILE"), out var file) && file < tab.Details.Files.Count)
            {
                tab.Details.SelectedFile = tab.Details.Files[file];
                await SettledAsync();
            }
        }

        if (int.TryParse(Env("SCROLL"), out var scrolls)
            && this.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(l => l.Name == "CommitList") is { Scroll: ScrollViewer list })
        {
            // Near the end the list asks for the next page; each page is a refresh the tab tracks.
            for (var i = 0; i < scrolls; i++)
            {
                list.ScrollToEnd();
                await SettledAsync();
            }
            list.Offset = list.Offset.WithY(list.Offset.Y - 200);
            await LaidOutAsync();
            if (vm.SelectedWorkspace.SelectedRepoTab is { } t)
                Console.WriteLine($"snapshot: {t.Rows.Count} rows, offset {list.Offset.Y:0}, extent {list.Extent.Height:0}");
        }

        Window target = this;
        var current = vm.SelectedWorkspace.SelectedRepoTab;
        if (Env("TERMINAL") == "1" && vm.SelectedWorkspace.SelectedTab is { } terminalTab)
        {
            terminalTab.Terminal.ShowCommand.Execute(null);
            await LaidOutAsync();
            await TerminalPanel.WhenStartedAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var terminal = TerminalPanel.GetVisualDescendants().OfType<Iciclecreek.Terminal.TerminalControl>()
                .Single(t => t.IsVisible);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var output = new System.Text.StringBuilder();
            void OnOutput(object? sender, Iciclecreek.Terminal.OutputReceivedEventArgs e)
            {
                output.Append(e.Output);
                if (output.ToString().Contains("Git Graph terminal ready", StringComparison.Ordinal)) ready.TrySetResult();
            }
            terminal.OutputReceived += OnOutput;
            try
            {
                await terminal.SendInputAsync(OperatingSystem.IsWindows()
                    ? "Get-Location; Write-Output ('Git Graph terminal ' + 'ready')\r"
                    : "pwd; tty; printf 'Git Graph terminal %s\\n' ready\r");
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally { terminal.OutputReceived -= OnOutput; }
        }
        if (Env("FULLDIFF") is "1" or "expanded" && current != null)
        {
            await current.Details.OpenFullDiffCommand.ExecuteAsync(null);
            target = OwnedWindows.LastOrDefault() ?? this;
            if (Env("FULLDIFF") == "expanded" && target.DataContext is FullDiffViewModel diff) diff.ExpandAllCommand.Execute(null);
        }
        if (Env("HISTORY") is "history" or "blame" && current != null)
        {
            if (Env("HISTORY") == "blame") current.Details.BlameCommand.Execute(null);
            else current.Details.FileHistoryCommand.Execute(null);
            target = OwnedWindows.LastOrDefault() ?? this;
            if (target.DataContext is FileHistoryViewModel history) await history.Loads.WhenIdleAsync();
        }
        if (Env("ABOUT") == "1")
        {
            _ = new AboutWindow().ShowDialog(this);
            await LaidOutAsync();
            target = OwnedWindows.LastOrDefault() ?? this;
        }
        if (Env("SHORTCUTS") is { Length: > 0 } filter)
        {
            RunShortcut("KeyboardShortcuts");
            await LaidOutAsync();
            target = OwnedWindows.LastOrDefault() ?? this;
            if (filter != "1" && target.DataContext is ShortcutsEditorViewModel editor) editor.Filter = filter;
        }
        await AvatarService.WhenLoadedAsync();
        await LaidOutAsync();

        // 1x by default; GITGRAPH_SNAPSHOT_SCALE=2 renders at a HiDPI scale to check sharpness.
        var scale = double.TryParse(Env("SCALE"), out var forced) ? forced : 1;
        var size = new PixelSize((int)(target.Bounds.Width * scale), (int)(target.Bounds.Height * scale));
        using var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bmp.Render(target);
        bmp.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>
    /// Waits until the tab on screen has nothing loading: its graph, details pane and contributors, or a folder's
    /// status and file. Laying out can start more (a tab loads when first shown), so it checks again after each pass.
    /// </summary>
    private async Task SettledAsync()
    {
        for (var pass = 0; pass < 2; pass++)
        {
            switch ((DataContext as MainWindowViewModel)?.SelectedWorkspace.SelectedTab)
            {
                case RepoTabViewModel repo:
                    await repo.Loads.WhenIdleAsync();
                    await repo.Details.Loads.WhenIdleAsync();
                    break;
                case FolderTabViewModel folder:
                    await folder.Loads.WhenIdleAsync();
                    break;
            }
            await LaidOutAsync();
        }
    }

    /// <summary>Lets layout run: work queued at Background priority comes after layout and rendering.</summary>
    private static Task LaidOutAsync() => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
}
