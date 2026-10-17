using System.Reflection;
using Avalonia.Controls;

namespace GitGraph.App.Services;

/// <summary>
/// Gives Wayland windows an app_id, so GNOME links them to gitgraph.desktop (name and icon in the dock) instead
/// of showing "Unknown". Avalonia 12.1's experimental Wayland backend never sets one, and doesn't expose its
/// xdg_toplevel; this finds the NWayland toplevel inside the window and calls its SetAppId. Does nothing under
/// X11, where the window class GitGraph already matches the desktop file's StartupWMClass.
/// </summary>
public static class WaylandAppId
{
    public const string AppId = "gitgraph";

    public static void Apply(Window window)
    {
        try
        {
            var impl = typeof(TopLevel).GetProperty("PlatformImpl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(window);
            if (impl is null || impl.GetType().Assembly.GetName().Name != "Avalonia.Wayland") return;
            if (FindToplevel(impl, depth: 3) is { } toplevel)
                toplevel.GetType().GetMethod("SetAppId", [typeof(string)])?.Invoke(toplevel, [AppId]);
        }
        catch (Exception e) when (e is TargetInvocationException or MemberAccessException or InvalidOperationException)
        {
            // Internals changed in a newer Avalonia; the window just keeps no app_id.
        }
    }

    /// <summary>Looks through the window's fields (and theirs) for NWayland's XdgToplevel.</summary>
    private static object? FindToplevel(object obj, int depth)
    {
        foreach (var field in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var value = field.GetValue(obj);
            if (value is null || value is string || field.FieldType.IsPrimitive) continue;
            var type = value.GetType();
            if (type.Name == "XdgToplevel" && type.Namespace?.StartsWith("NWayland", StringComparison.Ordinal) == true) return value;
            if (depth > 1 && type.Assembly == obj.GetType().Assembly && FindToplevel(value, depth - 1) is { } found) return found;
        }
        return null;
    }
}
