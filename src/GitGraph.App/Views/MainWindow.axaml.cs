using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class MainWindow : ShadUI.Window, IDialogService
{
    private static readonly LinearGradientBrush FadeRightMask = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Colors.Black, 0),
            new GradientStop(Colors.Black, 0.94),
            new GradientStop(Colors.Transparent, 1),
        }
    };

    private static readonly LinearGradientBrush FadeLeftMask = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Colors.Transparent, 0),
            new GradientStop(Colors.Black, 0.06),
            new GradientStop(Colors.Black, 1),
        }
    };

    private static readonly LinearGradientBrush FadeBothMask = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Colors.Transparent, 0),
            new GradientStop(Colors.Black, 0.06),
            new GradientStop(Colors.Black, 0.94),
            new GradientStop(Colors.Transparent, 1),
        }
    };

    private DateTime _lastActivated = DateTime.MinValue;
    private MainWindowViewModel? _vm;
    private bool _tabOverflow;
    private bool _tabFadeLeft;
    private bool _tabFadeRight;

    public MainWindow()
    {
        InitializeComponent();
        // On macOS, menus live in the system menu bar (NativeMenu.Menu) and traffic lights sit in the
        // sidebar/tab header row; Linux and Windows keep the in-window menu bar.
        if (OperatingSystem.IsMacOS())
        {
            TopBar.IsVisible = false;
            MacSidebarHeader.IsVisible = true;
            MacDetailsToggle.IsVisible = true;
        }
        Activated += (_, _) =>
        {
            // Files may have changed in an editor while we were in the background.
            if (DateTime.UtcNow - _lastActivated < TimeSpan.FromSeconds(2)) return;
            _lastActivated = DateTime.UtcNow;
            (DataContext as MainWindowViewModel)?.SelectedWorkspace.SelectedTab?.OnActivated();
        };
        InitShortcuts();
        TabStrip.AddHandler(PointerWheelChangedEvent, OnTabStripWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        TabStrip.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => UpdateTabOverflow());
        TabStrip.SizeChanged += (_, _) => UpdateTabOverflow();
        TabStrip.LayoutUpdated += (_, _) => UpdateTabOverflow();
        TabStrip.SelectionChanged += (_, _) =>
        {
            if (TabStrip.SelectedItem is not { } tab) return;
            TabStrip.ScrollIntoView(tab);
            UpdateTabOverflow();
            if (RepoTabHost.IsVisible) Motion.FadeIn(RepoTabHost);
            else if (FolderTabHost.IsVisible) Motion.FadeIn(FolderTabHost);
        };
    }

    private void UpdateTabOverflow()
    {
        if (TabStrip.Scroll is not ScrollViewer sv || !TabStrip.IsVisible)
        {
            if (_tabOverflow)
            {
                _tabOverflow = false;
                TabOverflowButton.IsVisible = false;
            }
            if (_tabFadeLeft || _tabFadeRight)
            {
                _tabFadeLeft = _tabFadeRight = false;
                TabStrip.OpacityMask = null;
            }
            return;
        }

        var overflow = sv.Extent.Width > sv.Viewport.Width + 1;
        var fadeLeft = overflow && sv.Offset.X > 1;
        var fadeRight = overflow && sv.Offset.X < sv.Extent.Width - sv.Viewport.Width - 1;

        if (overflow != _tabOverflow)
        {
            _tabOverflow = overflow;
            TabOverflowButton.IsVisible = overflow;
        }

        if (fadeLeft != _tabFadeLeft || fadeRight != _tabFadeRight)
        {
            _tabFadeLeft = fadeLeft;
            _tabFadeRight = fadeRight;
            TabStrip.OpacityMask = (fadeLeft, fadeRight) switch
            {
                (true, true) => FadeBothMask,
                (true, false) => FadeLeftMask,
                (false, true) => FadeRightMask,
                _ => null,
            };
        }
    }

    private void OnTabOverflowPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (TabOverflowButton.Flyout is { IsOpen: true } flyout) flyout.Hide();
        if (TabStrip.SelectedItem is { } tab) TabStrip.ScrollIntoView(tab);
    }

    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Visual source || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().Any(v => v is MenuItem or Button or ListBoxItem) == true) return;
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    // Double-clicking a preview tab keeps it open.
    private void OnTabDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) tab.Workspace.PinTabCommand.Execute(tab);
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
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as MainWindowViewModel;
        if (_vm != null)
        {
            _vm.Dialogs = this;
            _vm.PropertyChanged += OnVmChanged;
            SyncMacSidebarChrome();
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ShowSidebar))
        {
            SyncMacSidebarChrome();
            if (_vm?.ShowSidebar == true) Motion.SlideIn(SidebarHost, offsetX: -14f, offsetY: 0f);
        }
    }

    private void SyncMacSidebarChrome()
    {
        if (OperatingSystem.IsMacOS())
            MacCollapsedControls.IsVisible = !(_vm?.ShowSidebar ?? true);
    }

    private void OnExit(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnAbout(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => new AboutWindow().ShowDialog(this);

    private void OnNativeExit(object? sender, EventArgs e) => Close();

    private void OnNativeAbout(object? sender, EventArgs e) => new AboutWindow().ShowDialog(this);

    private void OnNativeShortcut(object? sender, EventArgs e)
    {
        if (sender is NativeMenuItem { CommandParameter: string id }) RunShortcut(id);
    }

    /// <summary>Menu items that run a shortcut command name it in their Tag.</summary>
    private void OnMenuCommand(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string id }) RunShortcut(id);
    }

    /// <summary>
    /// Where prompts open: over the window asked from (the full diff window, a history or reflog window), which
    /// would otherwise cover a prompt owned by this one.
    /// </summary>
    private Window DialogOwner => OwnedWindows.LastOrDefault(w => w.IsActive) ?? this;

    public Task<string[]?> PromptAsync(string title, string? message, IReadOnlyList<PromptField> fields, string confirmText) =>
        new PromptDialog(title, message, fields, confirmText, destructive: false).ShowDialog<string[]?>(DialogOwner);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false) =>
        await new PromptDialog(title, message, [], confirmText, destructive).ShowDialog<string[]?>(DialogOwner) is not null;

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

    public void ShowFileHistory(FileHistoryViewModel history) => new FileHistoryWindow { DataContext = history }.Show(this);

    public void ShowReflog(ReflogViewModel reflog) => new ReflogWindow { DataContext = reflog }.Show(this);

    public void ShowInteractiveRebase(InteractiveRebaseViewModel rebase) => new InteractiveRebaseWindow { DataContext = rebase }.ShowDialog(this);

    public void ShowOutput(string title, string text) => new OutputWindow(title, text).Show(this);

    public void ShowCustomCommands(CustomCommandsViewModel commands) => new CustomCommandsWindow { DataContext = commands }.ShowDialog(this);

    public void ShowFolder(FolderTabViewModel folder) => new FolderWindow { DataContext = folder }.Show(this);
}
