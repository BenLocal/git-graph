using Avalonia;
using Avalonia.Media;

namespace GitGraph.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        SkipVsyncWaitUnderWayland();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Under a Wayland desktop the app is an XWayland client, and Mesa's GLX waited for vertical sync in each buffer
    /// swap. Avalonia waits for a new window's first frame on the UI thread, so opening the full diff window froze the
    /// app for about a second while GNOME mapped it; without the wait it took 150 ms. The compositor still prevents
    /// tearing. Only Mesa reads vblank_mode, and a value the user set is kept.
    /// </summary>
    private static void SkipVsyncWaitUnderWayland()
    {
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 })
            // Environment.SetEnvironmentVariable doesn't reach native code on Linux; setenv does.
            setenv("vblank_mode", "0", overwrite: 0);
    }

    [System.Runtime.InteropServices.DllImport("libc", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    private static extern int setenv(string name, string value, int overwrite);

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
