using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Controls;

/// <summary>
/// Left margin for the blame editor: draws commit summary and author/date at the start of each run with
/// clean hairline dividers (no alternating row shading) and opens the commit in history when clicked.
/// </summary>
public sealed class BlameMargin : AbstractMargin
{
    private const double MarginWidth = 280;
    private readonly Action<BlameRowViewModel> _onClick;
    private IReadOnlyList<BlameRowViewModel> _rows = [];
    private BlameRowViewModel? _hovered;

    public BlameMargin(Action<BlameRowViewModel> onClick)
    {
        _onClick = onClick;
        Width = MarginWidth;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void SetRows(IReadOnlyList<BlameRowViewModel> rows)
    {
        _rows = rows;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    public override void Render(DrawingContext context)
    {
        if (TextView is not { VisualLinesValid: true } view || _rows.Count == 0) return;

        var rulePen = new Pen(BrushOf("RuleBrush", Brushes.Gray), 1);
        var textBrush = BrushOf("TextBrush", Brushes.White);
        var mutedBrush = BrushOf("MutedBrush", Brushes.Gray);
        var typeface = new Typeface(FontFamilyOf());

        foreach (var line in view.VisualLines)
        {
            var index = line.FirstDocumentLine.LineNumber - 1;
            if ((uint)index >= (uint)_rows.Count) continue;
            var row = _rows[index];
            var y = line.VisualTop - view.VerticalOffset;
            var h = line.Height;

            if (row.IsBlockStart && row.Number > 1)
                context.DrawLine(rulePen, new Point(0, y + 0.5), new Point(Bounds.Width, y + 0.5));

            if (!row.IsBlockStart) continue;

            var meta = new FormattedText(row.Meta, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11.5, mutedBrush);
            var metaWidth = row.Meta.Length > 0 ? meta.Width + 10 : 0;
            var summaryMax = Math.Max(40, Bounds.Width - 22 - metaWidth);
            var summary = new FormattedText(row.Summary, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, textBrush)
            {
                MaxTextWidth = summaryMax,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };

            var ty = y + (h - summary.Height) / 2;
            context.DrawText(summary, new Point(12, ty));
            if (row.Meta.Length > 0)
                context.DrawText(meta, new Point(Bounds.Width - 10 - meta.Width, y + (h - meta.Height) / 2));
        }

        context.DrawLine(rulePen, new Point(Bounds.Width - 0.5, 0), new Point(Bounds.Width - 0.5, Bounds.Height));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var row = RowAt(e.GetPosition(this).Y);
        if (ReferenceEquals(row, _hovered)) return;
        _hovered = row;
        ToolTip.SetTip(this, row?.Tooltip);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = null;
        ToolTip.SetTip(this, null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (RowAt(e.GetPosition(this).Y) is { } row)
        {
            _onClick(row);
            e.Handled = true;
        }
    }

    private BlameRowViewModel? RowAt(double y)
    {
        if (TextView is not { VisualLinesValid: true } view || _rows.Count == 0) return null;
        var docY = y + view.VerticalOffset;
        var line = view.VisualLines.FirstOrDefault(v => docY >= v.VisualTop && docY < v.VisualTop + v.Height);
        if (line is null) return null;
        var index = line.FirstDocumentLine.LineNumber - 1;
        return (uint)index < (uint)_rows.Count ? _rows[index] : null;
    }

    private IBrush BrushOf(string key, IBrush fallback) =>
        Application.Current?.TryFindResource(key, ActualThemeVariant, out var r) == true && r is IBrush b ? b : fallback;

    private static FontFamily FontFamilyOf() =>
        Application.Current?.TryFindResource("UiFont", out var r) == true && r is FontFamily f ? f : FontFamily.Default;
}
