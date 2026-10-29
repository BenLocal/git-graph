using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Controls;

public sealed class LanguageBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<LanguageRowViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<LanguageBar, IReadOnlyList<LanguageRowViewModel>?>(nameof(Items));

    public IReadOnlyList<LanguageRowViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    static LanguageBar() => AffectsRender<LanguageBar>(ItemsProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Items is null) return;
        using var clip = context.PushClip(new RoundedRect(new Rect(Bounds.Size), 5));
        double x = 0;
        foreach (var item in Items)
        {
            var width = Bounds.Width * item.Share;
            context.FillRectangle(item.Brush, new Rect(x, 0, width, Bounds.Height));
            x += width;
        }
    }
}
