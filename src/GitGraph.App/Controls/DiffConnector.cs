using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GitGraph.Core.Diff;

namespace GitGraph.App.Controls;

/// <summary>
/// The strip between the two sides of a diff: each change is a band from its lines on the left to its lines on the
/// right, as in Meld and WinMerge, so a change can be followed across even where the sides have scrolled apart.
/// </summary>
public sealed class DiffConnector : Control
{
    private DiffPane? _left, _right;
    private IReadOnlyList<DiffBlock> _blocks = [];

    public void Attach(DiffPane left, DiffPane right)
    {
        _left = left;
        _right = right;
        foreach (var view in new[] { left.TextView, right.TextView })
        {
            view.ScrollOffsetChanged += (_, _) => InvalidateVisual();
            view.VisualLinesChanged += (_, _) => InvalidateVisual();
        }
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public void Load(IReadOnlyList<DiffBlock> blocks)
    {
        _blocks = blocks;
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        if (_left is null || _right is null) return;
        var width = Bounds.Width;
        dc.FillRectangle(this.Brush("CanvasBrush"), new Rect(Bounds.Size));
        // Each side's text area may sit lower than this strip's top (the editors' padding); measure from there.
        var leftTop = _left.TextView.TranslatePoint(default, this)?.Y ?? 0;
        var rightTop = _right.TextView.TranslatePoint(default, this)?.Y ?? 0;
        using var clip = dc.PushClip(new Rect(Bounds.Size));
        foreach (var b in _blocks)
        {
            if (!b.Changed) continue;
            var (l1, l2) = Edges(_left, _left.Span(b), leftTop);
            var (r1, r2) = Edges(_right, _right.Span(b), rightTop);
            if (Math.Max(l2, r2) < 0 || Math.Min(l1, r1) > Bounds.Height) continue;

            var mid = width / 2;
            var band = new StreamGeometry();
            using (var g = band.Open())
            {
                g.BeginFigure(new Point(0, l1), true);
                g.CubicBezierTo(new Point(mid, l1), new Point(mid, r1), new Point(width, r1));
                g.LineTo(new Point(width, r2));
                g.CubicBezierTo(new Point(mid, r2), new Point(mid, l2), new Point(0, l2));
                g.EndFigure(true);
            }
            // Deleted lines only: red; added only: green; replaced: the hunk colours.
            var (fill, edge) = b.OldCount == 0 ? ("AddBgBrush", "AddFgBrush") : b.NewCount == 0 ? ("DelBgBrush", "DelFgBrush") : ("HunkBgBrush", "HunkFgBrush");
            var current = ReferenceEquals(b, _left.Current);
            dc.DrawGeometry(this.Brush(fill), new Pen(this.Brush(current ? "SignalBrush" : edge), current ? 1.5 : 1), band);
        }
    }

    // Top and bottom of a block's lines on one side, in this strip's coordinates.
    private static (double Top, double Bottom) Edges(DiffPane pane, (int Start, int Count) span, double origin)
    {
        var offset = pane.TextView.VerticalOffset;
        return (pane.Top(span.Start) - offset + origin, pane.Top(span.Start + span.Count) - offset + origin);
    }
}
