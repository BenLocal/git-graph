using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GitGraph.App.Controls;

/// <summary>
/// Weekly commit counts as rounded bars. The bar under the pointer brightens and its week shows in a tooltip.
/// With <see cref="ShowGrid"/>, faint gridlines carry value labels; with <see cref="Weeks"/>, months label the
/// bottom. Several charts can share <see cref="Maximum"/> so their heights compare.
/// </summary>
public sealed class ActivityChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<int>?> ValuesProperty =
        AvaloniaProperty.Register<ActivityChart, IReadOnlyList<int>?>(nameof(Values));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ActivityChart, double>(nameof(Maximum));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ActivityChart, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IReadOnlyList<DateTimeOffset>?> WeeksProperty =
        AvaloniaProperty.Register<ActivityChart, IReadOnlyList<DateTimeOffset>?>(nameof(Weeks));

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<ActivityChart, bool>(nameof(ShowGrid));

    private int _hover = -1;

    static ActivityChart()
    {
        AffectsRender<ActivityChart>(ValuesProperty, MaximumProperty, StrokeProperty, WeeksProperty, ShowGridProperty);
    }

    public ActivityChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Background = Brushes.Transparent;
    }

    public IReadOnlyList<int>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public IReadOnlyList<DateTimeOffset>? Weeks { get => GetValue(WeeksProperty); set => SetValue(WeeksProperty, value); }
    public bool ShowGrid { get => GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }

    /// <summary>Hit-testing needs a background even where no bar is drawn.</summary>
    public IBrush? Background { get; set; }

    private const double AxisHeight = 18;
    private double GutterWidth => ShowGrid ? 30 : 0;

    private (double Left, double Width, double Height, double Slot, double Max) Layout()
    {
        var values = Values ?? [];
        var left = GutterWidth;
        var width = Math.Max(0, Bounds.Width - left);
        var height = Math.Max(0, Bounds.Height - (Weeks is { Count: > 0 } ? AxisHeight : 0));
        var slot = values.Count == 0 ? 0 : width / values.Count;
        var max = Maximum > 0 ? Maximum : Math.Max(1, values.Count == 0 ? 1 : values.Max());
        return (left, width, height, slot, NiceCeiling(max));
    }

    /// <summary>Rounds the top of the scale up to 1, 2 or 5 × 10ⁿ, so gridline labels are round numbers.</summary>
    private static double NiceCeiling(double v)
    {
        if (v <= 4) return Math.Max(1, Math.Ceiling(v));
        var p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var m in new[] { 1.0, 2, 2.5, 5, 10 })
            if (m * p >= v) return m * p;
        return 10 * p;
    }

    private IBrush Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;

    public override void Render(DrawingContext ctx)
    {
        var values = Values;
        if (values is null || values.Count == 0) return;
        var (left, width, height, slot, max) = Layout();
        if (width <= 0 || height <= 4) return;
        ctx.FillRectangle(Background ?? Brushes.Transparent, new Rect(Bounds.Size));

        var rule = Resource("RuleBrush");
        var muted = Resource("MutedBrush");
        var typeface = new Typeface(FontFamily.Default);
        if (ShowGrid)
        {
            // Gridlines at 0, half and the top, with their values in the gutter.
            foreach (var f in new[] { 0.0, 0.5, 1.0 })
            {
                var y = Math.Round(height - f * (height - 4)) - 0.5;
                ctx.DrawLine(new Pen(rule, 1, f == 0 ? null : new DashStyle([3, 3], 0)), new Point(left, y), new Point(left + width, y));
                var label = new FormattedText((f * max).ToString("0.#", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, GitGraph.App.Services.FontScale.Of(10.5), muted);
                ctx.DrawText(label, new Point(left - 6 - label.Width, y - label.Height / 2));
            }
        }

        var color = (Stroke as ISolidColorBrush)?.Color ?? ((Resource("SignalBrush") as ISolidColorBrush)?.Color ?? Colors.Orange);
        var rest = new SolidColorBrush(color, _hover >= 0 ? 0.55 : 0.85);
        var hot = new SolidColorBrush(color);
        var barWidth = Math.Max(1, slot * (slot > 6 ? 0.68 : 0.8));
        var radius = Math.Min(3, barWidth / 2);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0) continue;
            var h = Math.Max(2, values[i] / max * (height - 4));
            var x = left + i * slot + (slot - barWidth) / 2;
            var bar = new Rect(x, height - h, barWidth, h);
            // Rounded top, square bottom: a rounded rectangle with its lower corners covered.
            ctx.DrawRectangle(i == _hover ? hot : rest, null, new RoundedRect(bar, radius, radius, 0, 0));
        }

        if (Weeks is { Count: > 0 } weeks) DrawMonths(ctx, weeks, left, slot, height, typeface, muted);
    }

    /// <summary>A label at each month's first week, thinned so labels don't collide; January shows the year.</summary>
    private void DrawMonths(DrawingContext ctx, IReadOnlyList<DateTimeOffset> weeks, double left, double slot, double top,
        Typeface typeface, IBrush muted)
    {
        var lastRight = double.NegativeInfinity;
        for (var i = 0; i < weeks.Count; i++)
        {
            if (i > 0 && weeks[i].Month == weeks[i - 1].Month) continue;
            var format = weeks[i].Month == 1 || i == 0 ? "MMM yyyy" : "MMM";
            var text = new FormattedText(weeks[i].ToString(format, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, GitGraph.App.Services.FontScale.Of(10.5), muted);
            var x = Math.Min(left + i * slot, Bounds.Width - text.Width);
            if (x < lastRight + 12) continue;
            ctx.DrawText(text, new Point(x, top + 4));
            lastRight = x + text.Width;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var values = Values;
        if (values is null || values.Count == 0) return;
        var (left, _, _, slot, _) = Layout();
        var i = slot <= 0 ? -1 : (int)((e.GetPosition(this).X - left) / slot);
        if (i < 0 || i >= values.Count) i = -1;
        if (i == _hover) return;
        _hover = i;
        InvalidateVisual();
        ToolTip.SetTip(this, i < 0 ? null : Describe(i, values[i]));
        ToolTip.SetIsOpen(this, i >= 0);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    private string Describe(int i, int count)
    {
        var commits = count == 1 ? "1 commit" : $"{count:N0} commits";
        return Weeks is { } weeks && i < weeks.Count
            ? $"{commits} in the week of {weeks[i].ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"
            : commits;
    }
}
