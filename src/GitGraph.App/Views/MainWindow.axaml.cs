using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class MainWindow : Window, IDialogService
{
    private DateTime _lastActivated = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        Activated += (_, _) =>
        {
            // Files may have changed in an editor while we were in the background.
            if (DateTime.UtcNow - _lastActivated < TimeSpan.FromSeconds(2)) return;
            _lastActivated = DateTime.UtcNow;
            (DataContext as MainWindowViewModel)?.SelectedWorkspace.SelectedTab?.OnActivated();
        };
        InitShortcuts();
        Opened += (_, _) => WaylandAppId.Apply(this);
        TabStrip.AddHandler(PointerWheelChangedEvent, OnTabStripWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        TabStrip.SelectionChanged += (_, _) =>
        {
            if (TabStrip.SelectedItem is { } tab) TabStrip.ScrollIntoView(tab);
        };
    }

    // Double-clicking a preview tab keeps it open.
    private void OnTabDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is RepoTabViewModel tab) tab.Workspace.PinTabCommand.Execute(tab);
    }

    /// <summary>The tab strip has no visible scrollbar, so a vertical wheel scrolls it sideways.</summary>
    private void OnTabStripWheel(object? sender, PointerWheelEventArgs e)
    {
        if (TabStrip.Scroll is not ScrollViewer sv) return;
        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
        sv.Offset = sv.Offset.WithX(Math.Clamp(sv.Offset.X - delta * 60, 0, Math.Max(0, sv.Extent.Width - sv.Viewport.Width)));
        e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm) vm.Dialogs = this;
    }

    private void OnExit(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    /// <summary>Menu items that run a shortcut command name it in their Tag.</summary>
    private void OnMenuCommand(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string id }) RunShortcut(id);
    }

    public Task<string[]?> PromptAsync(string title, string? message, IReadOnlyList<PromptField> fields, string confirmText) =>
        new PromptDialog(title, message, fields, confirmText, destructive: false).ShowDialog<string[]?>(this);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false) =>
        await new PromptDialog(title, message, [], confirmText, destructive).ShowDialog<string[]?>(this) is not null;

    public async Task<string?> PickFolderAsync(string title)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task CopyTextAsync(string text)
    {
        if (Clipboard is { } cb) await cb.SetTextAsync(text);
    }

    public void ShowDiff(FullDiffViewModel diff) => new DiffWindow { DataContext = diff }.Show(this);
}
