using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GitGraph.App.Services;

namespace GitGraph.App.Views;

/// <summary>
/// Records a shortcut: press a key combination, optionally a second one for a chord like VS Code's
/// Ctrl+K Ctrl+S, then Enter to save or Escape to cancel. Closes with the gesture string, or null.
/// </summary>
public sealed class KeyCaptureDialog : Window
{
    private readonly List<string> _parts = [];
    private DateTime _lastKey;
    private readonly TextBlock _keys;
    private readonly TextBlock _conflict;

    public KeyCaptureDialog(string commandTitle)
    {
        Title = "Record shortcut";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _keys = new TextBlock
        {
            Text = "Press the keys",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _conflict = new TextBlock { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "small" } };
        _conflict.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("DangerBrush"));

        var keycap = new Border { Classes = { "keycap", "large" }, HorizontalAlignment = HorizontalAlignment.Center, Child = _keys };
        Content = new StackPanel
        {
            Margin = new Thickness(24, 20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = commandTitle, FontSize = 16, FontWeight = FontWeight.SemiBold },
                keycap,
                _conflict,
                new TextBlock
                {
                    Text = "Press a second combination right after the first for a two-key shortcut. Enter saves, Escape cancels.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted", "small" },
                },
            },
        };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += (_, _) => WaylandAppId.Apply(this);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Close(null);
            return;
        }
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _parts.Count > 0)
        {
            Close(string.Join(" ", _parts));
            return;
        }
        if (ShortcutService.FromKeyEvent(e) is not { } g) return;

        // A second combination soon after the first makes a chord; otherwise start over.
        if (_parts.Count == 1 && DateTime.UtcNow - _lastKey < TimeSpan.FromSeconds(1.5)) _parts.Add(g);
        else
        {
            _parts.Clear();
            _parts.Add(g);
        }
        _lastKey = DateTime.UtcNow;

        var gesture = string.Join(" ", _parts);
        _keys.Text = ShortcutService.Format(gesture);
        var svc = ShortcutService.Instance;
        var other = svc.Commands.FirstOrDefault(c => svc.Gesture(c.Id) == gesture);
        _conflict.Text = other is null ? "" : $"Already used by {other.Title}. Saving gives both commands the same shortcut.";
    }
}
