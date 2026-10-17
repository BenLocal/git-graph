using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace GitGraph.App.Services;

/// <summary>What every window gets as it opens: the main window, the diff window and every dialog.</summary>
public static class WindowSetup
{
    public static void Register() =>
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Apply(window), RoutingStrategies.Direct);

    private static void Apply(Window window)
    {
        // Text the way GNOME draws it (subpixel RGB antialiasing, slight hinting), with baselines on whole
        // pixels: Avalonia's defaults leave text visibly softer than the rest of the desktop, most of all at
        // fractional scales such as 125%. Subpixel falls back to grey antialiasing over non-opaque backgrounds.
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.SubpixelAntialias);
        TextOptions.SetTextHintingMode(window, TextHintingMode.Light);
        TextOptions.SetBaselinePixelAlignment(window, BaselinePixelAlignment.Aligned);
        WaylandAppId.Apply(window);
    }
}
