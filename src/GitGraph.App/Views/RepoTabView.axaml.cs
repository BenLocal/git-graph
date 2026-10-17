using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.ComponentModel;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class RepoTabView : UserControl
{
    private DetailsViewModel? _details;
    private MainWindowViewModel? _main;
    // The panel height the user last dragged to, restored after hiding or maximizing.
    private GridLength _panelHeight = new(320);

    public RepoTabView()
    {
        InitializeComponent();
        // Tunnel so Ctrl+click is seen before the ListBox turns it into a selection toggle.
        CommitList.AddHandler(PointerPressedEvent, OnCommitPointerPressed, RoutingStrategies.Tunnel);
        CommitList.SelectionChanged += (_, _) =>
        {
            if (CommitList.SelectedItem is { } item) CommitList.ScrollIntoView(item);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_details != null) _details.PropertyChanged -= OnDetailsChanged;
        _details = (DataContext as RepoTabViewModel)?.Details;
        if (_details != null)
        {
            _details.PropertyChanged += OnDetailsChanged;
            // Another tab may have shown or hidden the shared panel since this one was last active.
            _details.SyncPanel();
        }
        ApplyPanelLayout();
    }

    // The panel's visibility is shared app state, toggled from the menu bar; follow it.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _main = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
        if (_main != null) _main.PropertyChanged += OnMainChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_main != null) _main.PropertyChanged -= OnMainChanged;
        _main = null;
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ShowDetailsPanel)) _details?.SyncPanel();
    }

    private void OnDetailsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DetailsViewModel.IsPanelVisible) or nameof(DetailsViewModel.IsMaximized))
            ApplyPanelLayout();
    }

    /// <summary>Rows: 1 column headings, 2 graph, 3 splitter, 4 details panel.</summary>
    private void ApplyPanelLayout()
    {
        var rows = Layout.RowDefinitions;
        var visible = _details?.IsPanelVisible ?? true;
        var maximized = visible && _details?.IsMaximized == true;

        // Remember the dragged height while the panel is in its normal state.
        if (rows[4].Height.IsAbsolute && rows[4].Height.Value > 0) _panelHeight = rows[4].Height;

        rows[1].Height = maximized ? new GridLength(0) : GridLength.Auto;
        rows[2].Height = maximized ? new GridLength(0) : GridLength.Star;
        rows[4].Height = !visible ? new GridLength(0) : maximized ? GridLength.Star : _panelHeight;
        Graph.IsVisible = !maximized;
        Headings.IsVisible = !maximized;
        DetailsSplitter.IsVisible = visible && !maximized;
        DetailsPane.IsVisible = visible;
    }

    public void FocusFind()
    {
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void OnCommitPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not RepoTabViewModel vm) return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || !e.GetCurrentPoint(CommitList).Properties.IsLeftButtonPressed) return;
        var row = (e.Source as Visual)?.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(c => c.DataContext)
            .OfType<CommitRowViewModel>()
            .FirstOrDefault();
        if (row is null) return;
        vm.SetCompare(row);
        e.Handled = true;
    }
}
