using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;
using Lucide.Avalonia;

namespace GitGraph.App.Views;

/// <summary>Help → About: the version from the release tag, the commit, and the platform.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.IsDevBuild ? $"Version {AppInfo.Version} (development build)" : $"Version {AppInfo.Version}";
        CommitText.Text = AppInfo.Commit ?? "unknown";
        RuntimeText.Text = AppInfo.Runtime;
        AvaloniaText.Text = AppInfo.Avalonia;
        OsText.Text = AppInfo.OperatingSystem;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    private async void OnOpenRepository(object? sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(AppInfo.RepositoryUrl, UriKind.Absolute, out var uri)) await Launcher.LaunchUriAsync(uri);
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(AppInfo.Summary);
        // The icon turns into a check for a moment to confirm the copy.
        CopyIcon.Kind = LucideIconKind.Check;
        DispatcherTimer.RunOnce(() => CopyIcon.Kind = LucideIconKind.Copy, CopyNote.Duration);
    }

    private string? _downloadUrl;

    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        UpdateText.IsVisible = true;
        UpdateText.Text = "Checking…";
        var result = await UpdateCheck.CheckAsync();
        UpdateText.Text = result.Message;
        _downloadUrl = result.DownloadUrl;
        DownloadButton.IsVisible = _downloadUrl != null;
        CheckButton.IsEnabled = true;
    }

    private async void OnDownload(object? sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(_downloadUrl, UriKind.Absolute, out var uri)) await Launcher.LaunchUriAsync(uri);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
