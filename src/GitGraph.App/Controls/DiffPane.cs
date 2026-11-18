using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using GitGraph.App.Services;
using GitGraph.Core.Diff;

namespace GitGraph.App.Controls;

/// <summary>
/// One side of a side-by-side diff in a read-only <see cref="TextEditor"/>: changed lines and words get the added or
/// deleted backgrounds, a change with no lines on this side is a line between its neighbours, the current change is
/// outlined, the code takes its syntax colours, and folded unchanged lines show as GitHub's expander rows.
/// </summary>
public sealed class DiffPane
{
    private readonly TextEditor _editor;
    private readonly bool _old;
    private IReadOnlyList<DiffLine> _lines = [];
    private IReadOnlyList<DiffBlock> _blocks = [];
    private DiffSyntax _syntax = DiffSyntax.Empty;
    private DiffBlock? _current;

    public DiffPane(TextEditor editor, bool old)
    {
        _editor = editor;
        _old = old;
        editor.TextArea.TextView.BackgroundRenderers.Add(new Backgrounds(this));
        _note = new LineNoteRenderer(editor);
        editor.TextArea.TextView.LineTransformers.Add(new SyntaxColours(editor, number =>
            number <= _lines.Count && (_old ? _lines[number - 1].OldLine : _lines[number - 1].NewLine) is { } n ? _syntax.Line(_old, n) : null));
        editor.ActualThemeVariantChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    public TextView TextView => _editor.TextArea.TextView;

    /// <summary>Shows one side's lines; <paramref name="blocks"/> come from <see cref="TextDiff.Blocks"/>.</summary>
    public void Load(IReadOnlyList<DiffLine> lines, IReadOnlyList<DiffBlock> blocks, DiffSyntax syntax)
    {
        _lines = lines;
        _blocks = blocks;
        _syntax = syntax;
        _current = null;
        _editor.Document = new TextDocument(string.Join('\n', lines.Select(l => l.Text)));
    }

    public DiffBlock? Current
    {
        get => _current;
        set
        {
            _current = value;
            TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    private IReadOnlySet<int> _commentLines = new HashSet<int>();

    /// <summary>The 0-based lines with review comments, marked with a bar at the left edge.</summary>
    public IReadOnlySet<int> CommentLines
    {
        get => _commentLines;
        set
        {
            _commentLines = value;
            TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    private readonly LineNoteRenderer _note;

    /// <summary>Faint text after the end of a 0-based line, like GitLens's blame of the caret's line.</summary>
    public (int Line, string Text)? Note
    {
        get => _note.Note;
        set => _note.Note = value;
    }

    /// <summary>Where a block starts and ends on this side, in lines.</summary>
    public (int Start, int Count) Span(DiffBlock b) => _old ? (b.OldStart, b.OldCount) : (b.NewStart, b.NewCount);

    /// <summary>The top of 0-based line <paramref name="line"/> in document coordinates; past the end, the bottom.</summary>
    public double Top(int line) =>
        line < _editor.Document.LineCount ? TextView.GetVisualTopByDocumentLine(line + 1) : TextView.DocumentHeight;

    internal IBrush Brush(string key) => _editor.Brush(key);

    private FoldingManager? _folds;

    /// <summary>
    /// Installs folding on the editor, with GitHub's expander rows in place of AvaloniaEdit's boxed placeholder: a
    /// folded stretch is a row on the hunk background with its title, and clicking the row opens it.
    /// </summary>
    public FoldingManager InstallFolds()
    {
        _folds = FoldingManager.Install(_editor.TextArea);
        // Ahead of AvaloniaEdit's generator, which stays: it connects the manager to the view, which collapses the lines.
        var generators = TextView.ElementGenerators;
        generators.Insert(generators.IndexOf(generators.OfType<FoldingElementGenerator>().First()), new FoldRows(this, _folds));
        return _folds;
    }

    public void UninstallFolds()
    {
        if (_folds is null) return;
        foreach (var rows in TextView.ElementGenerators.OfType<FoldRows>().ToList()) TextView.ElementGenerators.Remove(rows);
        FoldingManager.Uninstall(_folds);
        _folds = null;
    }

    private sealed class FoldRows(DiffPane pane, FoldingManager manager) : VisualLineElementGenerator
    {
        public override int GetFirstInterestedOffset(int startOffset) => manager.GetNextFoldedFoldingStart(startOffset);

        public override VisualLineElement? ConstructElement(int offset)
        {
            if (manager.GetFoldingsAt(offset).FirstOrDefault(f => f.IsFolded) is not { } fold) return null;
            var properties = new VisualLineElementTextRunProperties(CurrentContext.GlobalTextRunProperties);
            properties.SetForegroundBrush(pane.Brush("HunkFgBrush"));
            var text = FormattedTextElement.PrepareText(TextFormatter.Current, fold.Title, properties);
            return new FoldRow(fold, text, fold.EndOffset - offset);
        }
    }

    private sealed class FoldRow(FoldingSection fold, TextLine text, int length) : FormattedTextElement(text, length)
    {
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            fold.IsFolded = false;
            e.Handled = true;
        }
    }

    private void ApplyTheme()
    {
        ResourceBrushes.ApplyEditorTheme(_editor);
        TextView.Redraw();
    }

    private sealed class Backgrounds(DiffPane pane) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (!textView.VisualLinesValid) return;
            var width = textView.Bounds.Width;
            // Folded stretches: GitHub's expander rows, across the whole width.
            if (pane._folds is { } folds)
            {
                var hunk = pane.Brush("HunkBgBrush");
                foreach (var visual in textView.VisualLines)
                    if (folds.GetFoldingsAt(visual.FirstDocumentLine.Offset).Any(f => f.IsFolded))
                        dc.FillRectangle(hunk, new Rect(0, visual.VisualTop - textView.VerticalOffset, width, visual.Height));
            }

            var lineBrush = pane.Brush(pane._old ? "DelBgBrush" : "AddBgBrush");
            var wordBrush = pane.Brush(pane._old ? "DelWordBgBrush" : "AddWordBgBrush");
            foreach (var visual in textView.VisualLines)
            {
                var index = visual.FirstDocumentLine.LineNumber - 1;
                if (index >= pane._lines.Count || pane._lines[index] is not { Kind: DiffLineKind.Added or DiffLineKind.Deleted } line) continue;
                dc.FillRectangle(lineBrush, new Rect(0, visual.VisualTop - textView.VerticalOffset, width, visual.Height));
                if (line.Segments is not { } segments || segments.Sum(s => s.Text.Length) != line.Text.Length) continue;
                var offset = visual.FirstDocumentLine.Offset;
                foreach (var s in segments)
                {
                    if (s.Changed)
                        foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = offset, Length = s.Text.Length }))
                            dc.FillRectangle(wordBrush, r);
                    offset += s.Text.Length;
                }
            }

            // A change with nothing on this side: a line where the other side's lines would be.
            var marker = new Pen(pane.Brush(pane._old ? "AddFgBrush" : "DelFgBrush"), 2);
            foreach (var b in pane._blocks)
            {
                var (start, count) = pane.Span(b);
                if (!b.Changed || count > 0) continue;
                var y = pane.Top(start) - textView.VerticalOffset;
                dc.DrawLine(marker, new Point(0, y), new Point(width, y));
            }

            if (pane._commentLines.Count > 0)
            {
                var brush = pane.Brush("SignalBrush");
                foreach (var visual in textView.VisualLines)
                    if (pane._commentLines.Contains(visual.FirstDocumentLine.LineNumber - 1))
                        dc.FillRectangle(brush, new Rect(0, visual.VisualTop - textView.VerticalOffset, 3, visual.Height));
            }

            if (pane._current is { } current)
            {
                var (start, count) = pane.Span(current);
                var top = pane.Top(start) - textView.VerticalOffset;
                var bottom = pane.Top(start + count) - textView.VerticalOffset;
                var pen = new Pen(pane.Brush("SignalBrush"), 1.5);
                dc.DrawLine(pen, new Point(0, top), new Point(width, top));
                if (count > 0) dc.DrawLine(pen, new Point(0, bottom), new Point(width, bottom));
            }
        }
    }
}
