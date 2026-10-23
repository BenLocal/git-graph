using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using GitGraph.Core.Graph;

namespace GitGraph.App.Controls;

/// <summary>
/// Draws one row of the commit graph. Every row only draws its own top and bottom halves,
/// so the rows join up into continuous lines while the list virtualizes freely.
/// </summary>
public sealed class GraphCell : Control
{
    public const double LaneWidth = 16;
    public const double Padding = 8;

    public static readonly StyledProperty<GraphRow?> RowProperty =
        AvaloniaProperty.Register<GraphCell, GraphRow?>(nameof(Row));

    public static readonly StyledProperty<bool> IsHeadProperty =
        AvaloniaProperty.Register<GraphCell, bool>(nameof(IsHead));

    /// <summary>0–1 progress of the one-time "you are here" ripple around the HEAD station.</summary>
    public static readonly StyledProperty<double> RippleProperty =
        AvaloniaProperty.Register<GraphCell, double>(nameof(Ripple), 1);

    static GraphCell()
    {
        AffectsRender<GraphCell>(RowProperty, IsHeadProperty, RippleProperty);
    }

    public double Ripple
    {
        get => GetValue(RippleProperty);
        set => SetValue(RippleProperty, value);
    }

    // Rows rebuild on every load, so each load ripples once; scrolling a row back into view doesn't.
    private static readonly ConditionalWeakTable<GraphRow, object> Rippled = new();

    private static readonly Animation RippleAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(900),
        Easing = new CubicEaseOut(),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RippleProperty, 0.0) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RippleProperty, 1.0) } },
        },
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == RowProperty || change.Property == IsHeadProperty) && IsHead && Row is { } row
            && Motion.Enabled && !Rippled.TryGetValue(row, out _))
        {
            Rippled.Add(row, this);
            _ = RippleAnimation.RunAsync(this);
        }
    }

    public GraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>Marks the checked-out commit: drawn as an interchange with a signal halo.</summary>
    public bool IsHead
    {
        get => GetValue(IsHeadProperty);
        set => SetValue(IsHeadProperty, value);
    }

    public static double WidthFor(int lanes) => Padding * 2 + Math.Max(lanes, 1) * LaneWidth;

    public static double LaneX(int lane) => Padding + lane * LaneWidth + LaneWidth / 2;

    public GraphCell()
    {
        // Node fills use the canvas colour, which differs between night and day.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var row = Row;
        if (row is null) return;
        var h = Bounds.Height;
        var mid = h / 2;
        var canvas = this.Brush("CanvasBrush", Brushes.Black);

        foreach (var s in row.Segments)
        {
            var pen = LanePen(s.Color, s.Dashed);
            switch (s.Kind)
            {
                case SegmentKind.Pass:
                    ctx.DrawLine(pen, new Point(LaneX(s.FromLane), 0), new Point(LaneX(s.FromLane), h));
                    break;
                case SegmentKind.In:
                    DrawLink(ctx, pen, new Point(LaneX(s.FromLane), 0), new Point(LaneX(s.ToLane), mid));
                    break;
                case SegmentKind.Out:
                    DrawLink(ctx, pen, new Point(LaneX(s.FromLane), mid), new Point(LaneX(s.ToLane), h));
                    break;
            }
        }

        var c = new Point(LaneX(row.NodeLane), mid);
        var lane = LanePalette.Brush(row.NodeColor);
        if (row.NodeDashed)
        {
            var muted = this.Brush("MutedBrush", Brushes.Gray);
            ctx.DrawEllipse(canvas, new Pen(muted, 1.6, new DashStyle([2, 1.5], 0)), c, 4.5, 4.5);
        }
        else if (IsHead)
        {
            var signal = this.Brush("SignalBrush", Brushes.Orange);
            if (Ripple < 1)
            {
                // A ring that spreads out from the halo and fades, once per load.
                var r = 7.5 + 9 * Ripple;
                using (ctx.PushOpacity(1 - Ripple))
                    ctx.DrawEllipse(null, new Pen(signal, 1.5), c, r, r);
            }
            ctx.DrawEllipse(null, new Pen(signal, 2), c, 7.5, 7.5);
            ctx.DrawEllipse(lane, null, c, 4.5, 4.5);
        }
        else if (row.IsMerge)
        {
            ctx.DrawEllipse(lane, new Pen(canvas, 1.5), c, 3.6, 3.6);
        }
        else
        {
            ctx.DrawEllipse(canvas, new Pen(lane, 2.2), c, 4.2, 4.2);
        }
    }

    private static readonly Dictionary<(int, bool), IPen> PenCache = new();

    private static IPen LanePen(int color, bool dashed)
    {
        if (!PenCache.TryGetValue((color, dashed), out var pen))
        {
            pen = new ImmutablePen(LanePalette.Brush(color), 2.4,
                dashed ? new ImmutableDashStyle([2, 1.6], 0) : null, PenLineCap.Round, PenLineJoin.Round);
            PenCache[(color, dashed)] = pen;
        }
        return pen;
    }

    /// <summary>A vertical-in, vertical-out curve; a straight line when the lanes match.</summary>
    private static void DrawLink(DrawingContext ctx, IPen pen, Point from, Point to)
    {
        if (Math.Abs(from.X - to.X) < 0.5)
        {
            ctx.DrawLine(pen, from, to);
            return;
        }
        var dy = to.Y - from.Y;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(from, false);
            g.CubicBezierTo(new Point(from.X, from.Y + dy * 0.8), new Point(to.X, to.Y - dy * 0.8), to);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}
