using Tomlyn;
using Tomlyn.Model;

namespace GitGraph.Core.Workspaces;

/// <summary>An app in the "Open in" menus: its name, and the shell command that opens ${path} in it.</summary>
public sealed record OpenWithApp(string Name, string Command);

/// <summary>
/// The user's "Open in" apps, kept in open-with.toml next to state.json so they can be edited by hand.
/// Each <c>[[open]]</c> table has a <c>name</c> and a <c>command</c>.
/// </summary>
public static class OpenWithConfig
{
    /// <summary>Where the file lives: in the folder of the app's state file.</summary>
    public static string PathNextTo(string stateFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, "open-with.toml");

    /// <summary>
    /// What a new file holds. On macOS apps are opened by bundle id: an app started from the Dock doesn't get the
    /// shell's PATH, so `code` would not be found there.
    /// </summary>
    public static string DefaultToml(bool macOS) =>
        """
        # Apps in the "Open in" context menus. Each [[open]] adds one, in this order.
        # ${path} is replaced by the file's or folder's full path, quoted for the shell.

        """ + (macOS
            ? """
              [[open]]
              name = "VS Code"
              command = "open -b com.microsoft.VSCode ${path}"

              [[open]]
              name = "Winc"
              command = "open -b com.deeix.winc ${path}"

              """
            : """
              [[open]]
              name = "VS Code"
              command = "code ${path}"

              """);

    /// <summary>Reads the apps from <paramref name="path"/>, writing the default file first if there is none.</summary>
    /// <returns>The apps, and a message when the file could not be read; the apps are then empty.</returns>
    public static (IReadOnlyList<OpenWithApp> Apps, string? Error) Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, DefaultToml(OperatingSystem.IsMacOS()));
            }
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ([], $"Could not read {path}: {e.Message}");
        }
    }

    public static (IReadOnlyList<OpenWithApp> Apps, string? Error) Parse(string toml)
    {
        TomlTable root;
        try { root = TomlSerializer.Deserialize<TomlTable>(toml) ?? new TomlTable(); }
        catch (TomlException e) { return ([], e.Message); }

        if (!root.TryGetValue("open", out var open)) return ([], null);
        if (open is not TomlTableArray tables) return ([], "\"open\" must be a list of [[open]] tables.");

        var apps = new List<OpenWithApp>();
        foreach (var t in tables)
        {
            if (!t.TryGetValue("command", out var c) || c is not string command || string.IsNullOrWhiteSpace(command))
                return ([], "Every [[open]] needs a command, such as command = \"code ${path}\".");
            var name = t.TryGetValue("name", out var n) && n is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : command.Trim();
            apps.Add(new OpenWithApp(name, command));
        }
        return (apps, null);
    }
}
