using System.Diagnostics;

namespace GitGraph.App.Services;

/// <summary>Opens things in other apps.</summary>
public static class ExternalTools
{
    /// <summary>
    /// Opens a text file in the system's editor for it. macOS gets `open -t`: a .toml file often has no app of its own
    /// there. Returns an error message, or null.
    /// </summary>
    public static string? OpenInTextEditor(string path)
    {
        try
        {
            var psi = OperatingSystem.IsMacOS() ? new ProcessStartInfo("open") { ArgumentList = { "-t", path } }
                : OperatingSystem.IsLinux() ? new ProcessStartInfo("xdg-open") { ArgumentList = { path } }
                : new ProcessStartInfo(path) { UseShellExecute = true };
            using var _ = Process.Start(psi);
            return null;
        }
        catch (System.ComponentModel.Win32Exception e) { return $"Could not open {path}: {e.Message}"; }
    }
}
