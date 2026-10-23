using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GitGraph.App.Controls;

/// <summary>
/// Commits per day as a calendar of rounded squares, one column per week, like Claude Code's /stats and
/// GitHub's contribution graph. Shades of the signal colour show how busy a day was; hovering a day names it.
/// </summary>
public sealed class ContributionHeatmap : Control
{
    public static readonly StyledProperty<IReadOnlyDictionary<DateOnly, int>?> DaysProperty =
        AvaloniaProperty.Register<ContributionHeatmap, IReadOnlyDictionary<DateOnly, int>?>(nameof(Days));

    /// <summary>The first day to show; at most the last 53 weeks are drawn.</summary>
    public static readonly StyledProperty<DateTimeOffset?> FromProperty =
        AvaloniaProperty.Register<ContributionHeatmap, DateTimeOffset?>(nameof(From));

    private const double Cell = 12, Gap = 3, Left = 30, Top = 18, LegendHeight = 24;
    private const int MaxWeeks = 53;
    private (int Week, int Day) _hover = (-1, -1);

    static ContributionHeatmap()
    {
        AffectsRender<ContributionHeatmap>(DaysProperty, FromProperty);
        AffectsMeasure<ContributionHeatmap>(DaysProperty, FromProperty);
    }

    public ContributionHeatmap() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public IReadOnlyDictionary<DateOnly, int>? Days { get => GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public DateTimeOffset? From { get => GetValue(FromProperty); set => SetValue(FromProperty, value); }

    /// <summary>The Sunday the first column starts on, and how many columns end at this week.</summary>
    private (DateOnly Start, int Weeks) Range()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var thisWeek = today.AddDays(-(int)today.DayOfWeek);
        var from = From is { } f ? DateOnly.FromDateTime(f.DateTime)
            : Days is { Count: > 0 } d ? d.Keys.Min() : thisWeek;
        var weeks = Math.Clamp((thisWeek.DayNumber - from.AddDays(-(int)from.DayOfWeek).DayNumber) / 7 + 1, 1, MaxWeeks);
        return (thisWeek.AddDays(-7 * (weeks - 1)), weeks);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (_, weeks) = Range();
        return new Size(Left + weeks * (Cell + Gap), Top + 7 * (Cell + Gap) + LegendHeight);
    }

    /// <summary>Five shades: none, then four steps up to the busiest days (quartiles of the days with commits).</summary>
    private (IBrush[] Shades, int[] Steps) Scale()
    {
        var signal = (this.Brush("SignalBrush", Brushes.Gray) as ISolidColorBrush)?.Color ?? Colors.Orange;
        IBrush[] shades =
        [
            this.Brush("RaisedBrush", Brushes.Gray),
            new SolidColorBrush(signal, 0.3), new SolidColorBrush(signal, 0.5),
            new SolidColorBrush(signal, 0.75), new SolidColorBrush(signal, 1),
        ];
        var counts = (Days?.Values ?? []).Where(c => c > 0).OrderBy(c => c).ToList();
        int Q(double p) => counts.Count == 0 ? 1 : counts[(int)Math.Min(counts.Count - 1, p * counts.Count)];
        return (shades, [Q(0.25), Q(0.5), Q(0.75)]);
    }

    private static int Level(int count, int[] steps) =>
        count <= 0 ? 0 : count <= steps[0] ? 1 : count <= steps[1] ? 2 : count <= steps[2] ? 3 : 4;

    public override void Render(DrawingContext ctx)
    {
        var (start, weeks) = Range();
        var (shades, steps) = Scale();
        var muted = this.Brush("MutedBrush", Brushes.Gray);
        var typeface = new Typeface(FontFamily.Default);
        var today = DateOnly.FromDateTime(DateTime.Now);
        FormattedText Text(string s) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, GitGraph.App.Services.FontScale.Of(10.5), muted);

        foreach (var (row, name) in new[] { (1, "Mon"), (3, "Wed"), (5, "Fri") })
            ctx.DrawText(Text(name), new Point(0, Top + row * (Cell + Gap) - 1));

        var lastLabelRight = double.NegativeInfinity;
        for (var w = 0; w < weeks; w++)
        {
            var weekStart = start.AddDays(7 * w);
            var x = Left + w * (Cell + Gap);
            if (w == 0 || weekStart.Month != weekStart.AddDays(-7).Month)
            {
                var label = Text(weekStart.ToString(weekStart.Month == 1 ? "MMM yyyy" : "MMM", CultureInfo.InvariantCulture));
                if (x >= lastLabelRight + 6)
                {
                    ctx.DrawText(label, new Point(x, 0));
                    lastLabelRight = x + label.Width;
                }
            }
            for (var d = 0; d < 7; d++)
            {
                var day = weekStart.AddDays(d);
                if (day > today) break;
                var count = Days != null && Days.TryGetValue(day, out var c) ? c : 0;
                var rect = new Rect(x, Top + d * (Cell + Gap), Cell, Cell);
                ctx.DrawRectangle(shades[Level(count, steps)], null, new RoundedRect(rect, 2.5));
                if (_hover == (w, d))
                    ctx.DrawRectangle(null, new Pen(this.Brush("TextBrush", Brushes.Gray), 1.2), new RoundedRect(rect.Deflate(0.6), 2.5));
            }
        }

        // Legend under the grid, at the right: Less ▢▢▢▢▢ More
        var more = Text("More");
        var y = Top + 7 * (Cell + Gap) + 6;
        var right = Left + weeks * (Cell + Gap) - Gap;
        var xEnd = right - more.Width;
        ctx.DrawText(more, new Point(xEnd, y - 1));
        var sx = xEnd - 6 - 5 * (Cell + Gap) + Gap;
        for (var i = 0; i < 5; i++)
            ctx.DrawRectangle(shades[i], null, new RoundedRect(new Rect(sx + i * (Cell + Gap), y, Cell, Cell), 2.5));
        var less = Text("Less");
        ctx.DrawText(less, new Point(sx - 6 - less.Width, y - 1));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var (start, weeks) = Range();
        var w = (int)Math.Floor((p.X - Left) / (Cell + Gap));
        var d = (int)Math.Floor((p.Y - Top) / (Cell + Gap));
        var hit = w >= 0 && w < weeks && d >= 0 && d < 7 && p.X >= Left && p.Y >= Top ? (w, d) : (-1, -1);
        var day = hit.Item1 < 0 ? default : start.AddDays(7 * w + d);
        if (hit.Item1 >= 0 && day > DateOnly.FromDateTime(DateTime.Now)) hit = (-1, -1);
        if (hit == _hover) return;
        _hover = hit;
        InvalidateVisual();
        if (hit.Item1 < 0)
        {
            ToolTip.SetIsOpen(this, false);
            return;
        }
        var count = Days != null && Days.TryGetValue(day, out var c) ? c : 0;
        var what = count == 0 ? "No commits" : count == 1 ? "1 commit" : $"{count:N0} commits";
        ToolTip.SetTip(this, $"{what} on {day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture)}");
        ToolTip.SetIsOpen(this, true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = (-1, -1);
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }
}
