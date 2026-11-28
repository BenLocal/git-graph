using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.ViewModels;

/// <summary>An "Open in" submenu: the apps in open-with.toml for one file or folder, and editing that file.</summary>
public static class OpenWithMenu
{
    /// <summary>Reads the file each time, so a menu opened after an edit shows the edit.</summary>
    public static IReadOnlyList<CustomCommandItem> For(IAppHost host, string path)
    {
        var configPath = host.OpenWithConfigPath;
        var (apps, error) = OpenWithConfig.Load(configPath);
        var items = apps.Select(a => new CustomCommandItem(a.Name, new AsyncRelayCommand(() => OpenAsync(host, a, path)))).ToList();
        if (error is not null)
            items.Add(new CustomCommandItem("open-with.toml has an error…", new AsyncRelayCommand(() =>
            {
                host.ReportError($"open-with.toml: {error}");
                return Task.CompletedTask;
            })));
        items.Add(new CustomCommandItem("Edit open-with.toml…", new AsyncRelayCommand(() =>
        {
            if (ExternalTools.OpenInTextEditor(configPath) is { } failed) host.ReportError(failed);
            return Task.CompletedTask;
        })));
        return items;
    }

    private static async Task OpenAsync(IAppHost host, OpenWithApp app, string path)
    {
        var workDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
        try
        {
            var (exitCode, output) = await CustomCommandRunner.RunAsync(app.Command, workDir, new Dictionary<string, string> { ["path"] = path });
            if (exitCode != 0)
                host.ReportError($"Could not open in {app.Name} (exit code {exitCode}){(string.IsNullOrWhiteSpace(output) ? "." : ": " + output.Trim())}");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            host.ReportError($"Could not open in {app.Name}: {e.Message}");
        }
    }
}
