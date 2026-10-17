using System.Diagnostics;

namespace GitGraph.App.Services;

/// <summary>Opens things in other apps.</summary>
public static class ExternalTools
{
    /// <summary>
    /// Opens a folder or file in VS Code. An app started from the desktop may not have the user's
    /// PATH, so the usual install locations are tried too. Returns an error message, or null.
    /// </summary>
    public static string? OpenInVsCode(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = OperatingSystem.IsWindows()
            ? ["code.cmd", Path.Combine(local, "Programs", "Microsoft VS Code", "bin", "code.cmd"), @"C:\Program Files\Microsoft VS Code\bin\code.cmd"]
            : OperatingSystem.IsMacOS()
                ? ["code", "/usr/local/bin/code", "/opt/homebrew/bin/code", "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"]
                : ["code", Path.Combine(home, ".local", "bin", "code"), "/usr/bin/code", "/usr/share/code/bin/code", "/snap/bin/code"];

        foreach (var exe in candidates)
        {
            if (Path.IsPathRooted(exe) && !File.Exists(exe)) continue;
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add(path);
                using var _ = Process.Start(psi);
                return null;
            }
            catch (System.ComponentModel.Win32Exception) { /* not this one; try the next */ }
        }
        return "VS Code wasn't found. Install it, or put its \"code\" command on your PATH.";
    }
}
