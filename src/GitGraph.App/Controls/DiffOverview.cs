using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GitGraph.Core.Diff;

namespace GitGraph.App.Controls;

/// <summary>
/// A thin map of the whole diff beside the scroll bar: every change at its place in the file (the old file on the
/// left half, the new one on the right), with a frame around what is on screen. Clicking or dragging jumps there.
/// </summary>
public sealed class DiffOverview : Control
{
    private IReadOnlyList<DiffBlock> _blocks = [];
    private int _oldCount, _newCount;
    private double _viewTop, _viewBottom;

    /// <summary>Raised with a position in the new file, from 0 (top) to 1 (bottom).</summary>
    public event Action<double>? Navigate;

    public DiffOverview()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public void Load(IReadOnlyList<DiffBlock> blocks, int oldCount, int newCount)
    {
        _blocks = blocks;
        _oldCount = Math.Max(1, oldCount);
        _newCount = Math.Max(1, newCount);
        InvalidateVisual();
    }

    /// <summary>The part of the new file on screen, as fractions of it.</summary>
    public void SetViewport(double top, double bottom)
    {
        _viewTop = top;
        _viewBottom = bottom;
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var (w, h) = (Bounds.Width, Bounds.Height);
        dc.FillRectangle(this.Brush("SurfaceBrush"), new Rect(Bounds.Size));
        var half = w / 2;
        foreach (var b in _blocks)
        {
            if (!b.Changed) continue;
            var key = b.OldCount == 0 ? "AddFgBrush" : b.NewCount == 0 ? "DelFgBrush" : "HunkFgBrush";
            var brush = this.Brush(key);
            dc.FillRectangle(brush, Mark(b.OldStart, b.OldCount, _oldCount, 1, h, half - 1));
            dc.FillRectangle(brush, Mark(b.NewStart, b.NewCount, _newCount, half + 1, h, half - 2));
        }
        var top = _viewTop * h;
        var frame = new Rect(0.5, top + 0.5, w - 1, Math.Max(4, (_viewBottom - _viewTop) * h) - 1);
        dc.DrawRectangle(this.Brush("HoverWashBrush"), new Pen(this.Brush("MutedBrush"), 1), frame);
    }

    // A change on one half, at least 2px tall so a one-line change in a long file still shows.
    private static Rect Mark(int start, int count, int total, double x, double height, double width)
    {
        var y = start / (double)total * height;
        return new Rect(x, Math.Min(y, height - 2), Math.Max(1, width), Math.Max(2, count / (double)total * height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Jump(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this) Jump(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Jump(PointerEventArgs e)
    {
        if (Bounds.Height > 0) Navigate?.Invoke(Math.Clamp(e.GetPosition(this).Y / Bounds.Height, 0, 1));
        e.Handled = true;
    }
}
