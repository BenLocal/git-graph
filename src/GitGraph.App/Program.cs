using Avalonia;
using Avalonia.Media;

namespace GitGraph.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .With(new FontManagerOptions { DefaultFamilyName = "avares://GitGraph/Assets/Fonts#IBM Plex Sans" })
            .LogToTrace();
}
