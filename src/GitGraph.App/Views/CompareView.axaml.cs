using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using GitGraph.App.Controls;
using GitGraph.App.ViewModels;
using GitGraph.Core.Diff;

namespace GitGraph.App.Views;

/// <summary>
/// Two files side by side, each in its own read-only editor (so text selects across lines and copies as it is in the
/// file). The sides scroll together, keeping the line in the middle of one opposite its counterpart in the other.
/// </summary>
public partial class CompareView : UserControl
{
    private readonly DiffPane _old, _new;
    // Made for each pair of documents: a folding manager stays with the document it was installed on.
    private FoldingManager? _oldFolds, _newFolds;
    private readonly List<(FoldViewModel Fold, FoldingSection Old, FoldingSection New)> _folds = [];
    private FullDiffViewModel? _vm;
    // The side the user is scrolling; the other side's own scroll events are ignored until it has followed.
    private TextEditor? _scrolling;

    public CompareView()
    {
        InitializeComponent();
        _old = new DiffPane(OldEditor, old: true);
        _new = new DiffPane(NewEditor, old: false);
        Connector.Attach(_old, _new);
        Overview.Navigate += OnOverviewNavigate;

        OldEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnScrolled(OldEditor, NewEditor);
        NewEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnScrolled(NewEditor, OldEditor);
        OldEditor.TextArea.TextView.VisualLinesChanged += (_, _) => SyncFolds(fromOld: true);
        NewEditor.TextArea.TextView.VisualLinesChanged += (_, _) => SyncFolds(fromOld: false);
        OldEditor.TextArea.Caret.PositionChanged += (_, _) => _vm?.SetCaret(true, OldEditor.TextArea.Caret.Line - 1);
        NewEditor.TextArea.Caret.PositionChanged += (_, _) => _vm?.SetCaret(false, NewEditor.TextArea.Caret.Line - 1);
        AddMenu(OldEditor);
        AddMenu(NewEditor);
    }

    /// <summary>
    /// Right-click puts the caret on the line clicked, and the menu acts on the caret's line: comment on it (on a
    /// commit's diff) or copy the selection.
    /// </summary>
    private void AddMenu(TextEditor editor)
    {
        editor.TextArea.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(editor.TextArea).Properties.IsRightButtonPressed || editor.Document is not { LineCount: > 0 }) return;
            var view = editor.TextArea.TextView;
            var y = e.GetPosition(view).Y + view.VerticalOffset;
            var line = view.GetDocumentLineByVisualTop(Math.Clamp(y, 0, Math.Max(0, view.DocumentHeight - 1)));
            // Inside the selection, keep it for Copy.
            if (!editor.TextArea.Selection.IsEmpty && editor.TextArea.Selection.Segments.Any(s => s.StartOffset <= line.EndOffset && s.EndOffset >= line.Offset))
                return;
            editor.TextArea.Caret.Line = line.LineNumber;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var comment = new MenuItem { Header = "Comment on this line…" };
        comment.Click += (_, _) => _ = _vm?.CommentOnLineAsync(editor == OldEditor, editor.TextArea.Caret.Line - 1);
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => editor.Copy();
        var menu = new ContextMenu { Items = { comment, copy } };
        menu.Opening += (_, _) =>
        {
            comment.IsVisible = _vm?.CanComment == true;
            copy.IsEnabled = !editor.TextArea.Selection.IsEmpty;
        };
        editor.ContextMenu = menu;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmChanged;
            _vm.RevealRequested -= Reveal;
            _vm.LineRevealRequested -= RevealLine;
            foreach (var f in _folds) f.Fold.PropertyChanged -= OnFoldChanged;
        }
        _vm = DataContext as FullDiffViewModel;
        _folds.Clear();
        if (_oldFolds != null) FoldingManager.Uninstall(_oldFolds);
        if (_newFolds != null) FoldingManager.Uninstall(_newFolds);
        _oldFolds = _newFolds = null;
        if (_vm is null) return;

        _old.Load(_vm.OldLines, _vm.Blocks, _vm.Syntax);
        _new.Load(_vm.NewLines, _vm.Blocks, _vm.Syntax);
        _oldFolds = FoldingManager.Install(OldEditor.TextArea);
        _newFolds = FoldingManager.Install(NewEditor.TextArea);
        Connector.Load(_vm.Blocks);
        Overview.Load(_vm.Blocks, _vm.OldLines.Count, _vm.NewLines.Count);
        foreach (var f in _vm.Folds)
        {
            _folds.Add((f, Fold(_oldFolds, OldEditor, f.OldStart, f), Fold(_newFolds, NewEditor, f.NewStart, f)));
            f.PropertyChanged += OnFoldChanged;
        }
        _vm.PropertyChanged += OnVmChanged;
        _vm.RevealRequested += Reveal;
        _vm.LineRevealRequested += RevealLine;
        ShowCommentLines();
        // The first change, once the editors know their sizes.
        Dispatcher.UIThread.Post(() => { if (_vm is { HasChanges: true, CurrentChange: < 0 } vm) vm.GoToChange(0); }, DispatcherPriority.Background);
    }

    private static FoldingSection Fold(FoldingManager manager, TextEditor editor, int start, FoldViewModel fold)
    {
        var doc = editor.Document;
        var section = manager.CreateFolding(doc.GetLineByNumber(start + 1).Offset, doc.GetLineByNumber(start + fold.Count).EndOffset);
        section.Title = fold.Label;
        section.IsFolded = !fold.IsExpanded;
        return section;
    }

    private void ShowCommentLines()
    {
        _old.CommentLines = _vm?.OldCommentLines ?? new HashSet<int>();
        _new.CommentLines = _vm?.NewCommentLines ?? new HashSet<int>();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FullDiffViewModel.FileComments)) ShowCommentLines();
        if (e.PropertyName != nameof(FullDiffViewModel.CurrentChange) || _vm is null) return;
        var current = _vm.CurrentChange >= 0 ? _vm.Changes[_vm.CurrentChange] : null;
        _old.Current = current;
        _new.Current = current;
        Connector.InvalidateVisual();
    }

    // Expand all / Collapse all, or a fold opened on the other side.
    private void OnFoldChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var (fold, old, @new) in _folds)
            if (fold == sender)
            {
                old.IsFolded = !fold.IsExpanded;
                @new.IsFolded = !fold.IsExpanded;
            }
    }

    // A fold opened or closed with the margin's marker on one side: the other side and the view model follow.
    private void SyncFolds(bool fromOld)
    {
        foreach (var (fold, old, @new) in _folds)
        {
            var section = fromOld ? old : @new;
            if (section.IsFolded == !fold.IsExpanded) continue;
            fold.IsExpanded = !section.IsFolded;
        }
        UpdateOverviewViewport();
    }

    private void OnScrolled(TextEditor source, TextEditor other)
    {
        UpdateOverviewViewport();
        if (_scrolling != null && _scrolling != source) return;
        _scrolling = source;
        var from = source.TextArea.TextView;
        var to = other.TextArea.TextView;
        var fromOld = source == OldEditor;
        var anchor = from.VerticalOffset + from.Bounds.Height / 2;
        var mapped = Map(fromOld ? _old : _new, fromOld ? _new : _old, fromOld, anchor);
        var target = mapped - to.Bounds.Height / 2;
        if (Math.Abs(target - to.VerticalOffset) > 0.5 || Math.Abs(from.HorizontalOffset - to.HorizontalOffset) > 0.5)
            ScrollTo(other, Math.Max(0, target), from.HorizontalOffset);
        Dispatcher.UIThread.Post(() => _scrolling = null, DispatcherPriority.Background);
    }

    /// <summary>
    /// The document y on <paramref name="to"/> that matches <paramref name="y"/> on <paramref name="from"/>: lines of
    /// unchanged stretches match one to one, and a change's lines are spread evenly over the other side's.
    /// </summary>
    private double Map(DiffPane from, DiffPane to, bool fromOld, double y)
    {
        if (_vm is null) return y;
        var view = from.TextView;
        var lineCount = view.Document?.LineCount ?? 0;
        if (lineCount == 0) return y;
        var docLine = view.GetDocumentLineByVisualTop(Math.Clamp(y, 0, Math.Max(0, view.DocumentHeight - 1)));
        var index = docLine.LineNumber - 1;
        var top = from.Top(index);
        var height = from.Top(index + 1) - top;
        var position = index + (height > 0 ? Math.Clamp((y - top) / height, 0, 1) : 0);

        var mapped = (double)(fromOld ? _vm.NewLines.Count : _vm.OldLines.Count);
        foreach (var b in _vm.Blocks)
        {
            var (s, c) = fromOld ? (b.OldStart, b.OldCount) : (b.NewStart, b.NewCount);
            var (ds, dc) = fromOld ? (b.NewStart, b.NewCount) : (b.OldStart, b.OldCount);
            if (c == 0 || position >= s + c) continue;
            mapped = ds + (position - s) * dc / c;
            break;
        }
        var line = (int)Math.Floor(mapped);
        var lineTop = to.Top(line);
        return lineTop + (mapped - line) * (to.Top(line + 1) - lineTop);
    }

    /// <summary>Scrolls a change to a third of the way down and puts the caret on its first line.</summary>
    private void Reveal(DiffBlock block)
    {
        // Lead with a side that has lines in the change; the other side follows the scroll.
        var (editor, pane, start) = block.NewCount > 0 ? (NewEditor, _new, block.NewStart) : (OldEditor, _old, block.OldStart);
        var line = Math.Min(start + 1, editor.Document.LineCount);
        editor.TextArea.Caret.Line = line;
        editor.TextArea.Caret.Column = 1;
        // The caret may already be there, which raises no change; the line details still need to show it.
        _vm?.SetCaret(editor == OldEditor, line - 1);
        _scrolling = null;
        ScrollTo(editor, Math.Max(0, pane.Top(start) - pane.TextView.Bounds.Height / 3));
        editor.Focus();
    }

    /// <summary>Shows a commented line: opens its fold, scrolls it a third of the way down and puts the caret on it.</summary>
    private void RevealLine(bool old, int line)
    {
        var (editor, pane) = old ? (OldEditor, _old) : (NewEditor, _new);
        if (line < 0 || line >= editor.Document.LineCount) return;
        foreach (var (fold, _, _) in _folds)
        {
            var start = old ? fold.OldStart : fold.NewStart;
            if (line >= start && line < start + fold.Count) fold.IsExpanded = true;
        }
        editor.TextArea.Caret.Line = line + 1;
        editor.TextArea.Caret.Column = 1;
        _vm?.SetCaret(old, line);
        _scrolling = null;
        ScrollTo(editor, Math.Max(0, pane.Top(line) - pane.TextView.Bounds.Height / 3));
        editor.Focus();
    }

    private void OnOverviewNavigate(double fraction)
    {
        if (_vm is null) return;
        var line = (int)(fraction * _vm.NewLines.Count);
        _scrolling = null;
        ScrollTo(NewEditor, Math.Max(0, _new.Top(line) - NewEditor.TextArea.TextView.Bounds.Height / 2));
    }

    /// <summary>
    /// Scrolls an editor through its scroll viewer: AvaloniaEdit 12's <c>ScrollToVerticalOffset</c> and
    /// <c>ScrollToHorizontalOffset</c> do nothing.
    /// </summary>
    private static void ScrollTo(TextEditor editor, double y, double? x = null)
    {
        if (editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } viewer)
            viewer.Offset = new Vector(x ?? viewer.Offset.X, y);
    }

    private void UpdateOverviewViewport()
    {
        var view = NewEditor.TextArea.TextView;
        if (_vm is null || view.Document is null || view.DocumentHeight <= 0) return;
        var total = Math.Max(1, _vm.NewLines.Count);
        double LineAt(double y) =>
            view.GetDocumentLineByVisualTop(Math.Clamp(y, 0, Math.Max(0, view.DocumentHeight - 1))).LineNumber - 1;
        Overview.SetViewport(LineAt(view.VerticalOffset) / total, (LineAt(view.VerticalOffset + view.Bounds.Height) + 1) / total);
    }
}
