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
        // X11 (XWayland on a Wayland desktop) by default: on GNOME at 125% its text looked sharper to the user
        // than Avalonia's experimental native Wayland backend. GITGRAPH_BACKEND=wayland opts into Wayland, which
        // falls back to X11 by itself if it can't start.
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 }
            && Environment.GetEnvironmentVariable("GITGRAPH_BACKEND") == "wayland")
            builder = builder.UseWaylandWithFallback();
        return builder
#if DEBUG
            .WithDeveloperTools()
#endif
            .With(new FontManagerOptions { DefaultFamilyName = "avares://GitGraph/Assets/Fonts#IBM Plex Sans" })
            .LogToTrace();
    }
}
