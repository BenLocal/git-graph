using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using GitGraph.App.Services;
using GitGraph.Core.Diff;

namespace GitGraph.App.Controls;

/// <summary>
/// One side of a side-by-side diff in a read-only <see cref="TextEditor"/>: changed lines and words get the added or
/// deleted backgrounds, a change with no lines on this side is a line between its neighbours, the current change is
/// outlined, and the code takes its syntax colours.
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
        editor.TextArea.TextView.LineTransformers.Add(new Colours(this));
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

    /// <summary>Where a block starts and ends on this side, in lines.</summary>
    public (int Start, int Count) Span(DiffBlock b) => _old ? (b.OldStart, b.OldCount) : (b.NewStart, b.NewCount);

    /// <summary>The top of 0-based line <paramref name="line"/> in document coordinates; past the end, the bottom.</summary>
    public double Top(int line) =>
        line < _editor.Document.LineCount ? TextView.GetVisualTopByDocumentLine(line + 1) : TextView.DocumentHeight;

    internal IBrush Brush(string key) =>
        _editor.TryFindResource(key, _editor.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Transparent;

    private void ApplyTheme()
    {
        // The app's canvas and line-number colours, and the code viewer's selection, on which text keeps its colours.
        _editor.Background = Brush("CanvasBrush");
        _editor.LineNumbersForeground = Brush("MutedBrush");
        _editor.TextArea.SelectionBrush = Brush("EditorSelectionBrush");
        _editor.TextArea.SelectionForeground = null;
        _editor.TextArea.SelectionBorder = null;
        TextView.Redraw();
    }

    private sealed class Backgrounds(DiffPane pane) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (!textView.VisualLinesValid) return;
            var width = textView.Bounds.Width;
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

    private sealed class Colours(DiffPane pane) : DocumentColorizingTransformer
    {
        private readonly Dictionary<Color, IBrush> _brushes = [];

        protected override void ColorizeLine(DocumentLine line)
        {
            var index = line.LineNumber - 1;
            if (index >= pane._lines.Count) return;
            var number = pane._old ? pane._lines[index].OldLine : pane._lines[index].NewLine;
            if (number is not { } n || pane._syntax.Line(pane._old, n) is not { } spans) return;
            var light = pane._editor.ActualThemeVariant == ThemeVariant.Light;
            foreach (var span in spans)
            {
                var start = Math.Min(span.Start, line.Length);
                var end = Math.Min(span.Start + span.Length, line.Length);
                if (end <= start) continue;
                var color = light ? span.Light : span.Dark;
                if (!_brushes.TryGetValue(color, out var brush)) _brushes[color] = brush = new SolidColorBrush(color);
                ChangeLinePart(line.Offset + start, line.Offset + end, e =>
                {
                    e.TextRunProperties.SetForegroundBrush(brush);
                    if (span.Style != FontStyle.Normal || span.Weight != FontWeight.Normal)
                    {
                        var face = e.TextRunProperties.Typeface;
                        e.TextRunProperties.SetTypeface(new Typeface(face.FontFamily, span.Style, span.Weight));
                    }
                });
            }
        }
    }
}
