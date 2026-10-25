using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GitGraph.App.Services;

namespace GitGraph.App.Controls;

/// <summary>
/// A commit author's avatar from <see cref="AvatarService"/>. Until the image arrives, or when avatars are off
/// or offline, it shows the author's initials on a colour picked from the email; the identicon style draws
/// GitHub's kind of pattern instead.
/// </summary>
public sealed class Avatar : Control
{
    public static readonly StyledProperty<string?> EmailProperty =
        AvaloniaProperty.Register<Avatar, string?>(nameof(Email));

    public static readonly StyledProperty<string?> AuthorNameProperty =
        AvaloniaProperty.Register<Avatar, string?>(nameof(AuthorName));

    private Bitmap? _image;
    private FormattedText? _initials;
    // The identicon's colour and its 5 × 5 cells, from the email.
    private (Color Color, bool[] Cells)? _identicon;

    static Avatar()
    {
        AffectsRender<Avatar>(EmailProperty, AuthorNameProperty);
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

    // Switching styles takes effect on the avatars already on screen.
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
    public string? AuthorName
    {
        get => GetValue(AuthorNameProperty);
        set => SetValue(AuthorNameProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AuthorNameProperty || change.Property == EmailProperty) _initials = null;
        if (change.Property == EmailProperty) _identicon = null;
        if (change.Property != EmailProperty) return;
        _image = null;
        if (Email is { Length: > 0 } email) _ = LoadAsync(email);
    }

    private async Task LoadAsync(string email)
    {
        var style = AvatarService.Style;
        var image = await AvatarService.GetAsync(email);
        // The row may have been reused for another commit, or the style changed, while the image loaded.
        if (image is null || Email != email || AvatarService.Style != style) return;
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

        if (AvatarService.Style == AvatarService.Identicons)
        {
            DrawIdenticon(ctx, rect, circle, Email);
            return;
        }

        ctx.DrawGeometry(new SolidColorBrush(ColorFor(Email)), null, circle);
        _initials ??= new FormattedText(Initials(AuthorName, Email), System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            size * 0.42, Brushes.White);
        ctx.DrawText(_initials, new Point(rect.Center.X - _initials.Width / 2, rect.Center.Y - _initials.Height / 2));
    }

    /// <summary>
    /// A pattern like GitHub's default avatar: 5 × 5 cells mirrored left to right, in one colour, picked from the
    /// email's MD5 (as Gravatar keys pictures), on the soft disc behind every avatar.
    /// </summary>
    private void DrawIdenticon(DrawingContext ctx, Rect rect, Geometry circle, string email)
    {
        if (_identicon is null)
        {
            var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
            var cells = new bool[25];
            for (var i = 0; i < 15; i++)
            {
                var (col, row) = (i / 5, i % 5);
                cells[row * 5 + col] = cells[row * 5 + 4 - col] = (hash[2 + i / 8] >> (i % 8) & 1) == 1;
            }
            _identicon = (HslColor.ToRgb((hash[0] << 8 | hash[1]) % 360, 0.55, 0.52), cells);
        }
        var (color, on) = _identicon.Value;
        ctx.DrawGeometry(this.Brush("RaisedBrush", Brushes.LightGray), null, circle);
        // Inset so the corner cells stay inside the circle.
        var cell = rect.Width * 0.64 / 5;
        var origin = rect.Center - new Point(cell * 2.5, cell * 2.5);
        var brush = new SolidColorBrush(color);
        for (var i = 0; i < 25; i++)
            if (on[i]) ctx.FillRectangle(brush, new Rect(origin.X + i % 5 * cell, origin.Y + i / 5 * cell, cell + 0.5, cell + 0.5));
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
