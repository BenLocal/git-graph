using Avalonia;
using Avalonia.Media;

namespace GitGraph.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        // On a Wayland desktop, talk to Wayland directly: under XWayland with fractional scaling (GNOME at 125%)
        // the compositor shrinks the app's 2x window, which blurs text and icons. Avalonia's Wayland backend is
        // still experimental; GITGRAPH_BACKEND=x11 goes back to XWayland, and it falls back by itself on failure.
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 }
            && Environment.GetEnvironmentVariable("GITGRAPH_BACKEND") != "x11")
            builder = builder.UseWaylandWithFallback();
        return builder
#if DEBUG
            .WithDeveloperTools()
#endif
            .With(new FontManagerOptions { DefaultFamilyName = "avares://GitGraph/Assets/Fonts#IBM Plex Sans" })
            .LogToTrace();
    }
}
