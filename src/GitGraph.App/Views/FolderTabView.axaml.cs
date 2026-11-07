using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using GitGraph.App.Controls;
using GitGraph.App.ViewModels;
using GitGraph.Core.Navigation;

namespace GitGraph.App.Views;

public partial class FolderTabView : UserControl
{
    private FolderTabViewModel? _vm;
    private readonly LineNoteRenderer _note;
    private readonly OccurrenceRenderer _occurrences;
    private CancellationTokenSource? _occurrencesCts;
    private readonly LinkUnderlineRenderer _link;
    private CancellationTokenSource? _linkCts;
    private Point? _pointer;
    private readonly OccurrenceRenderer _previewMark;

    public FolderTabView()
    {
        InitializeComponent();
        // The view model's colours, from the same TextMate grammars (VS Code's Dark+ / Light+) as the diffs.
        Editor.TextArea.TextView.LineTransformers.Add(new SyntaxColours(Editor, n => _vm?.FileSyntax?.GetValueOrDefault(n)));
        // Room between the line numbers and the code.
        foreach (var margin in Editor.TextArea.LeftMargins.Concat(Preview.TextArea.LeftMargins).OfType<AvaloniaEdit.Editing.LineNumberMargin>())
            margin.Margin = new Avalonia.Thickness(4, 0, 18, 0);
        // Dark+ or Light+ colours follow the app's night and day themes.
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        // Who last changed the caret's line, at its end.
        _note = new LineNoteRenderer(Editor);
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            _vm?.SetCaret(Editor.TextArea.Caret.Line - 1, Editor.CaretOffset);
            _vm?.Loads.Track(ShowOccurrencesAsync());
        };
        // The uses of the name at the caret, washed.
        _occurrences = new OccurrenceRenderer(Editor);
        // Ctrl+click (Cmd+click on macOS) goes to the definition; a right-click moves the caret to the name clicked,
        // so the context menu's lookups act on it. Tunnel, before the text area starts a selection.
        Editor.TextArea.AddHandler(PointerPressedEvent, OnEditorPointerPressed, RoutingStrategies.Tunnel);
        // While Ctrl (Cmd) is held, the name under the pointer is underlined and the pointer becomes a hand.
        _link = new LinkUnderlineRenderer(Editor);
        Editor.TextArea.PointerMoved += (_, e) =>
        {
            _pointer = e.GetPosition(Editor);
            UpdateLink(e.KeyModifiers);
        };
        Editor.TextArea.PointerExited += (_, _) =>
        {
            _pointer = null;
            UpdateLink(KeyModifiers.None);
        };
        Editor.AddHandler(KeyDownEvent, (_, e) => UpdateLink(e.KeyModifiers), RoutingStrategies.Tunnel);
        Editor.AddHandler(KeyUpEvent, (_, e) => UpdateLink(e.KeyModifiers), RoutingStrategies.Tunnel);
        // The results list's preview: the picked result's file, coloured as the viewer, the result washed.
        Preview.TextArea.TextView.LineTransformers.Add(new SyntaxColours(Preview, n => _vm?.PreviewSyntax?.GetValueOrDefault(n)));
        _previewMark = new OccurrenceRenderer(Preview);
        // Each folder remembers how wide its tree was dragged.
        TreeSplitter.DragCompleted += (_, _) => _vm?.SaveTreeWidth(Columns.ColumnDefinitions[0].ActualWidth);
        // The query box steers the results list: arrows pick, Enter jumps, Esc closes. Tunnel, before the box moves its caret.
        SearchBox.AddHandler(KeyDownEvent, OnSearchBoxKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Opens go to file, go to symbol or find in files, for their shortcuts and the search button.</summary>
    public void StartSearch(SearchKind kind)
    {
        if (_vm is null) return;
        _vm.StartSearch(kind);
        // The box shows once the list is laid out.
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Background);
    }

    private void OnSearchMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: SearchKind kind }) StartSearch(kind);
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        var items = _vm.NavResults;
        var at = _vm.SelectedNavItem is { } selected ? items.IndexOf(selected) : -1;
        switch (e.Key)
        {
            case Key.Down or Key.Up when items.Count > 0:
                at = Math.Clamp(at + (e.Key == Key.Down ? 1 : -1), 0, items.Count - 1);
                _vm.SelectedNavItem = items[at];
                NavList.ScrollIntoView(at);
                break;
            case Key.Enter when _vm.SelectedNavItem is { } item:
                _vm.OpenCommand.Execute(item);
                break;
            case Key.Escape:
                _vm.CloseNavCommand.Execute(null);
                Editor.Focus();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Looks up the name at the caret, for the navigation shortcuts and the editor's context menu.</summary>
    public void Navigate(NavKind kind)
    {
        if (!Editor.IsVisible || _vm is null) return;
        _vm.Loads.Track(_vm.NavigateAsync(kind, Editor.CaretOffset));
    }

    /// <summary>Puts the caret at an offset of the open file, for the snapshot hook.</summary>
    public void MoveCaret(int offset)
    {
        Editor.CaretOffset = Math.Clamp(offset, 0, Editor.Document.TextLength);
        Editor.TextArea.Caret.BringCaretToView();
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Editor.TextArea);
        if (Editor.GetPositionFromPoint(e.GetPosition(Editor)) is not { } pos) return;
        var offset = Editor.Document.GetOffset(pos.Location);
        if (point.Properties.IsLeftButtonPressed && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            Editor.TextArea.ClearSelection();
            Editor.CaretOffset = offset;
            e.Handled = true;
            Navigate(NavKind.Definition);
        }
        else if (point.Properties.IsRightButtonPressed && !Editor.TextArea.Selection.Contains(offset))
        {
            Editor.TextArea.ClearSelection();
            Editor.CaretOffset = offset;
        }
    }

    /// <summary>Underlines the name under the pointer while Ctrl or Cmd is held, and nothing otherwise.</summary>
    private void UpdateLink(KeyModifiers modifiers)
    {
        var held = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        int? offset = held && _pointer is { } p && Editor.GetPositionFromPoint(p) is { } pos ? Editor.Document.GetOffset(pos.Location) : null;
        if (offset is { } o && _link.Span is { } shown && o >= shown.Start && o <= shown.End) return;
        _linkCts?.Cancel();
        _link.Span = null;
        Editor.TextArea.TextView.ClearValue(CursorProperty);
        if (offset is null || _vm is null) return;
        var cts = _linkCts = new CancellationTokenSource();
        _vm.Loads.Track(ShowLinkAsync(_vm, offset.Value, cts.Token));
    }

    private async Task ShowLinkAsync(FolderTabViewModel vm, int offset, CancellationToken ct)
    {
        try
        {
            var span = await vm.NameAtAsync(offset, ct);
            if (ct.IsCancellationRequested || span is null) return;
            _link.Span = span;
            Editor.TextArea.TextView.Cursor = new Cursor(StandardCursorType.Hand);
        }
        catch (OperationCanceledException) { }
    }

    private void OnNavMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: NavKind kind }) Navigate(kind);
    }

    private async Task ShowOccurrencesAsync()
    {
        _occurrencesCts?.Cancel();
        var cts = _occurrencesCts = new CancellationTokenSource();
        if (_vm is null) return;
        try
        {
            var spans = await _vm.OccurrencesAsync(Editor.CaretOffset, cts.Token);
            if (!cts.IsCancellationRequested) _occurrences.Spans = spans.Count > 1 ? spans : [];
        }
        catch (OperationCanceledException) { }
    }

    private void OnNavListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm?.CloseNavCommand.Execute(null);
            Editor.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && NavList.SelectedItem is NavItem item)
        {
            _vm?.OpenCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnNavItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: NavItem item }) _vm?.OpenCommand.Execute(item);
    }

    private void ShowPreviewText()
    {
        _previewMark.Spans = [];
        Preview.Document = new TextDocument(_vm?.PreviewText ?? "");
        ShowPreviewItem();
    }

    /// <summary>Washes the picked result in the preview and scrolls it a third of the way down.</summary>
    private void ShowPreviewItem()
    {
        if (_vm?.PreviewItem is not { } item || _vm.PreviewText is null) return;
        var doc = Preview.Document;
        var offset = Math.Clamp(item.Offset, 0, doc.TextLength);
        var end = Math.Clamp(item.Offset + item.Length, offset, doc.TextLength);
        var line = doc.GetLineByOffset(offset);
        _previewMark.Spans = [new TextSpan(offset, end, line.LineNumber - 1, offset - line.Offset)];
        ScrollToThird(Preview, offset);
    }

    /// <summary>
    /// Scrolls an editor so an offset's line sits a third of the way down. Waits for a layout pass, as a new document
    /// has no height before it; the editor's own scroll viewer is the one that moves (pitfall 2026-11-05).
    /// </summary>
    private static void ScrollToThird(AvaloniaEdit.TextEditor editor, int offset)
    {
        var doc = editor.Document;
        Dispatcher.UIThread.Post(() =>
        {
            if (editor.Document != doc || offset > doc.TextLength) return;
            var top = editor.TextArea.TextView.GetVisualTopByDocumentLine(doc.GetLineByOffset(offset).LineNumber);
            if (editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } viewer)
                viewer.Offset = new Vector(0, Math.Max(0, top - viewer.Viewport.Height / 3));
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Puts the caret where a jump asked for, once its file is the one on screen: the name selected, a third of the
    /// way down the editor.
    /// </summary>
    private void ApplyReveal()
    {
        if (_vm?.Reveal is not { } r || r.FilePath != _vm.FilePath || _vm.FileText is null) return;
        _vm.Reveal = null;
        var doc = Editor.Document;
        var offset = Math.Clamp(r.Offset, 0, doc.TextLength);
        var length = Math.Clamp(r.Length, 0, doc.TextLength - offset);
        Editor.CaretOffset = offset;
        Editor.TextArea.Selection = Selection.Create(Editor.TextArea, offset, offset + length);
        Editor.Focus();
        ScrollToThird(Editor, offset);
    }

    /// <summary>Opens the editor's search bar, for the Find shortcut while a folder tab is showing.</summary>
    public void OpenSearch()
    {
        if (!Editor.IsVisible) return;
        Editor.Focus();
        Editor.SearchPanel?.Open();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as FolderTabViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        // This view outlives its folder tabs; a selection left in the tree would keep a closed tab's files alive.
        else Tree.SelectedItem = null;
        ShowText();
        ShowPreviewText();
        ShowLineBlame();
        if (_vm != null) Columns.ColumnDefinitions[0].Width = new Avalonia.Controls.GridLength(_vm.TreeWidth);
        // The tree resets each new row's selection (and the binding copies that back), and its SelectedItem
        // doesn't reach nested rows; so once the rows exist, select the remembered file's row directly.
        Tree.LayoutUpdated -= RestoreSelection;
        _restoring = _vm?.SelectedNode;
        _restoredY = null;
        if (_restoring != null) Tree.LayoutUpdated += RestoreSelection;
    }

    /// <summary>
    /// Matches the rows to a selection the view model made (a jump to another file): a restored row's selection is a
    /// local value, which the view model's binding no longer reaches, and a row in a folder just opened doesn't exist
    /// yet, so it is selected and scrolled to once a layout pass makes it.
    /// </summary>
    private void FollowSelection()
    {
        if (_vm?.SelectedNode is not { } selected) return;
        foreach (var row in Tree.GetVisualDescendants().OfType<TreeViewItem>())
            if (row.DataContext is FileNodeViewModel n && row.IsSelected != n.IsSelected) row.IsSelected = n.IsSelected;
        if (Tree.TreeContainerFromItem(selected) is TreeViewItem { IsSelected: true }) return;
        _restoring = selected;
        _restoredY = null;
        Tree.LayoutUpdated -= RestoreSelection;
        Tree.LayoutUpdated += RestoreSelection;
    }

    // The remembered file being selected, and where its row was last scrolled to; the restore is done once a layout
    // pass leaves the row there, or as soon as another file is picked.
    private FileNodeViewModel? _restoring;
    private double? _restoredY;

    /// <summary>
    /// After each layout pass until the tree settles: selects the remembered file's row once it exists, and scrolls it
    /// to the middle (the tree may not know its full height on the first passes, so the middle moves until then).
    /// </summary>
    private void RestoreSelection(object? sender, EventArgs e)
    {
        if (_restoring is not { } node || _vm?.SelectedNode != node)
        {
            Tree.LayoutUpdated -= RestoreSelection;
            return;
        }
        if (Tree.TreeContainerFromItem(node) is not TreeViewItem row) return;
        row.IsSelected = true;
        var y = CenterInTree(row);
        if (y is null || y == _restoredY) Tree.LayoutUpdated -= RestoreSelection;
        _restoredY = y;
    }

    /// <summary>Scrolls a restored selection to the middle of the tree rather than just inside its edge.</summary>
    /// <returns>The offset scrolled to, or null when the row can't be placed.</returns>
    private double? CenterInTree(TreeViewItem row)
    {
        if (Tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { Content: Visual content } sv) return null;
        if (row.TranslatePoint(new Point(0, 0), content) is not { } at) return null;
        var target = Math.Clamp(at.Y - (sv.Viewport.Height - row.Bounds.Height) / 2, 0, Math.Max(0, sv.Extent.Height - sv.Viewport.Height));
        sv.Offset = sv.Offset.WithY(target);
        return target;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderTabViewModel.FileText)) ShowText();
        if (e.PropertyName == nameof(FolderTabViewModel.FileSyntax)) Editor.TextArea.TextView.Redraw();
        if (e.PropertyName == nameof(FolderTabViewModel.LineBlame)) ShowLineBlame();
        if (e.PropertyName == nameof(FolderTabViewModel.Reveal)) ApplyReveal();
        if (e.PropertyName == nameof(FolderTabViewModel.SelectedNode)) FollowSelection();
        if (e.PropertyName == nameof(FolderTabViewModel.PreviewText)) ShowPreviewText();
        if (e.PropertyName == nameof(FolderTabViewModel.PreviewSyntax)) Preview.TextArea.TextView.Redraw();
        if (e.PropertyName == nameof(FolderTabViewModel.PreviewItem)) ShowPreviewItem();
        // The results list takes the keys while it is open.
        // A search's query box keeps them instead (StartSearch focuses it).
        if (e.PropertyName == nameof(FolderTabViewModel.IsNavOpen) && _vm is { IsNavOpen: true, IsSearching: false })
            Dispatcher.UIThread.Post(() => NavList.Focus(), DispatcherPriority.Background);
        // A newly opened file fades in, to show the content was replaced.
        if (e.PropertyName is nameof(FolderTabViewModel.FileText) or nameof(FolderTabViewModel.Image)) Motion.FadeIn(FileArea);
    }

    private void ShowLineBlame() => _note.Note = _vm?.LineBlame is { } n ? (n.Line, n.Text) : null;

    private void ShowText()
    {
        _occurrencesCts?.Cancel();
        _occurrences.Spans = [];
        _linkCts?.Cancel();
        _link.Span = null;
        Editor.TextArea.TextView.ClearValue(CursorProperty);
        Editor.Document = new TextDocument(_vm?.FileText ?? "");
        Editor.ScrollToHome();
        ApplyReveal();
    }

    private void ApplyTheme()
    {
        ResourceBrushes.ApplyEditorTheme(Editor);
        Editor.TextArea.TextView.Redraw();
        ResourceBrushes.ApplyEditorTheme(Preview);
        Preview.TextArea.TextView.Redraw();
    }
}
