using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace GitGraph.App.Services;

/// <summary>What every window gets as it opens and as it closes: the main window, the diff window and every dialog.</summary>
public static class WindowSetup
{
    public static void Register()
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Apply(window), RoutingStrategies.Direct);
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Release(window), RoutingStrategies.Direct);
    }

    /// <summary>
    /// Under Fcitx, Avalonia 12.1 never collects some closed windows: each X11 window's Fcitx input method subscribes
    /// to the session bus connection and doesn't unsubscribe when the window closes (found with dotnet-dump gcroot).
    /// Dropping a closed window's content and data lets everything in it go (editors, documents, view models), so
    /// what is left behind is an empty window rather than a diff or a history.
    /// </summary>
    private static void Release(Window window)
    {
        // The leaked window's last frame still holds its controls, and a list's rows their items (as content and as
        // data context), so every control lets go of its data first.
        foreach (var element in window.GetVisualDescendants().OfType<StyledElement>().ToList())
        {
            if (element is ItemsControl list) list.ItemsSource = null;
            if (element is ContentPresenter presenter) presenter.Content = null;
            element.DataContext = null;
        }
        window.Content = null;
        window.DataContext = null;
    }

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
