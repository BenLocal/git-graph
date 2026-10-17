using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GitGraph.App.Controls;

/// <summary>Draws a stroke icon defined on a 24-unit grid, scaled to the control's size.</summary>
public sealed class IconView : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<IconView, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<IconView>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<IconView, double>(nameof(StrokeThickness), 1.7);

    static IconView()
    {
        AffectsRender<IconView>(DataProperty, ForegroundProperty, StrokeThicknessProperty);
        WidthProperty.OverrideDefaultValue<IconView>(16);
        HeightProperty.OverrideDefaultValue<IconView>(16);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        if (Data is null || Foreground is null) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        // Keep the visual stroke weight constant whatever the icon size.
        var pen = new Pen(Foreground, StrokeThickness / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale)))
            ctx.DrawGeometry(null, pen, Data);
    }
}
