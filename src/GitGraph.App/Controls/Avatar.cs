using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GitGraph.App.Services;

namespace GitGraph.App.Controls;

/// <summary>
/// A commit author's avatar from <see cref="AvatarService"/>. Until the image arrives, or when
/// avatars are off or offline, it shows the author's initials on a colour picked from the email.
/// </summary>
public sealed class Avatar : Control
{
    public static readonly StyledProperty<string?> EmailProperty =
        AvaloniaProperty.Register<Avatar, string?>(nameof(Email));

    public static readonly StyledProperty<string?> NameProperty =
        AvaloniaProperty.Register<Avatar, string?>(nameof(Name));

    private Bitmap? _image;
    private FormattedText? _initials;

    static Avatar()
    {
        AffectsRender<Avatar>(EmailProperty, NameProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AvatarService.StyleChanged += OnStyleChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        AvatarService.StyleChanged -= OnStyleChanged;
    }

    // Switching between robots and initials takes effect on the avatars already on screen.
    private void OnStyleChanged()
    {
        _image = null;
        InvalidateVisual();
        if (Email is { Length: > 0 } email) _ = LoadAsync(email);
    }

    public string? Email
    {
        get => GetValue(EmailProperty);
        set => SetValue(EmailProperty, value);
    }

    /// <summary>The author's name, for the initials shown while there is no image.</summary>
    public new string? Name
    {
        get => GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NameProperty || change.Property == EmailProperty) _initials = null;
        if (change.Property != EmailProperty) return;
        _image = null;
        if (Email is { Length: > 0 } email) _ = LoadAsync(email);
    }

    private async Task LoadAsync(string email)
    {
        var image = await AvatarService.GetAsync(email);
        // The row may have been reused for another commit while the image loaded.
        if (image is null || Email != email) return;
        _image = image;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        if (string.IsNullOrEmpty(Email)) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var rect = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        var circle = new EllipseGeometry(rect);

        if (_image is { } image)
        {
            // Robohash robots have transparent backgrounds; a soft disc keeps the avatar round.
            if (this.TryFindResource("RaisedBrush", ActualThemeVariant, out var r) && r is IBrush disc)
                ctx.DrawGeometry(disc, null, circle);
            using (ctx.PushGeometryClip(circle))
                ctx.DrawImage(image, new Rect(image.Size), rect);
            return;
        }

        ctx.DrawGeometry(new SolidColorBrush(ColorFor(Email)), null, circle);
        _initials ??= new FormattedText(Initials(Name, Email), System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            size * 0.42, Brushes.White);
        ctx.DrawText(_initials, new Point(rect.Center.X - _initials.Width / 2, rect.Center.Y - _initials.Height / 2));
    }

    private static string Initials(string? name, string email)
    {
        var words = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var letters = words.Length switch
        {
            0 => email[..1],
            1 => words[0][..1],
            _ => $"{words[0][0]}{words[^1][0]}",
        };
        return letters.ToUpperInvariant();
    }

    /// <summary>A mid-tone hue from the email, so white initials read on it in both themes.</summary>
    private static Color ColorFor(string email)
    {
        var h = 2166136261u;
        foreach (var ch in email.Trim().ToLowerInvariant()) { h ^= ch; h *= 16777619u; }
        return HslColor.ToRgb(h % 360, 0.45, 0.48);
    }
}

internal static class HslColor
{
    public static Color ToRgb(double hue, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = (hue / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
