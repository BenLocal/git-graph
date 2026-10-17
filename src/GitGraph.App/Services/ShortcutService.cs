using System.ComponentModel;
using Avalonia.Input;

namespace GitGraph.App.Services;

/// <summary>A command that can have a keyboard shortcut. <see cref="DefaultGesture"/> follows VS Code where it has the same action.</summary>
public sealed record ShortcutCommand(string Id, string Category, string Title, string? DefaultGesture);

/// <summary>
/// The app's keyboard shortcuts: the defaults, the user's overrides, and lookups that menus and tooltips bind to.
/// A gesture is one or two key combinations separated by a space, like "Ctrl+B" or VS Code's chords "Ctrl+K Ctrl+S".
/// </summary>
public sealed class ShortcutService
{
    public static ShortcutService Instance { get; } = new();

    public IReadOnlyList<ShortcutCommand> Commands { get; } =
    [
        new("ToggleSidebar", "View", "Toggle sidebar", "Ctrl+B"),
        new("TogglePanel", "View", "Toggle commit details panel", "Ctrl+J"),
        new("MaximizePanel", "View", "Maximize or restore the panel", null),
        new("ToggleDiffLayout", "View", "Switch diff between unified and split", null),
        new("CycleTheme", "View", "Switch theme", null),
        new("Find", "Repository", "Find commits, or search the open file", "Ctrl+F"),
        new("FindNext", "Repository", "Next match", "F3"),
        new("FindPrevious", "Repository", "Previous match", "Shift+F3"),
        new("Refresh", "Repository", "Refresh", "F5"),
        new("Fetch", "Repository", "Fetch from all remotes", null),
        new("Pull", "Repository", "Pull", null),
        new("Push", "Repository", "Push", null),
        new("Sync", "Repository", "Sync (pull, then push)", null),
        new("CloseTab", "Tabs", "Close tab", "Ctrl+W"),
        new("CloseAllTabs", "Tabs", "Close all tabs", "Ctrl+K Ctrl+W"),
        new("KeepTabOpen", "Tabs", "Keep preview tab open", "Ctrl+K Enter"),
        new("NextTab", "Tabs", "Next tab", "Ctrl+PageDown"),
        new("PreviousTab", "Tabs", "Previous tab", "Ctrl+PageUp"),
        .. Enumerable.Range(1, 9).Select(n => new ShortcutCommand($"Tab{n}", "Tabs", $"Go to tab {n}", $"Alt+D{n}")),
        new("AddFolder", "Workspace", "Add folder to workspace", null),
        new("NewWorkspace", "Workspace", "New workspace", null),
        new("KeyboardShortcuts", "App", "Keyboard shortcuts", "Ctrl+K Ctrl+S"),
        new("Quit", "App", "Quit", "Ctrl+Q"),
    ];

    /// <summary>User changes by command id; an empty string means the shortcut was removed.</summary>
    private readonly Dictionary<string, string> _overrides = [];

    private ShortcutService()
    {
        Keys = new Lookup<KeyGesture?>(id => Gesture(id) is { } g && !g.Contains(' ') ? KeyGesture.Parse(g) : null);
        Labels = new Lookup<string>(id => Format(Gesture(id)));
        Tips = new Lookup<string>(id =>
        {
            var title = Commands.FirstOrDefault(c => c.Id == id)?.Title ?? id;
            return Gesture(id) is { } g ? $"{title} ({Format(g)})" : title;
        });
    }

    /// <summary>Menu items' InputGesture. Two-key chords have no single KeyGesture, so they show in tooltips only.</summary>
    public Lookup<KeyGesture?> Keys { get; }

    /// <summary>"Ctrl+K Ctrl+S" as shown to people, or "" when unassigned.</summary>
    public Lookup<string> Labels { get; }

    /// <summary>A button's tooltip: the command's title with its shortcut.</summary>
    public Lookup<string> Tips { get; }

    /// <summary>Raised after a shortcut changes, so the settings can be saved.</summary>
    public event Action? Changed;

    public void Load(IReadOnlyDictionary<string, string> overrides)
    {
        _overrides.Clear();
        foreach (var (id, g) in overrides)
            if (Commands.Any(c => c.Id == id) && (g.Length == 0 || TryParse(g, out _))) _overrides[id] = g;
        Notify(raiseChanged: false);
    }

    public IReadOnlyDictionary<string, string> Overrides => _overrides;

    public string? DefaultGesture(string id) => Commands.FirstOrDefault(c => c.Id == id)?.DefaultGesture;

    public string? Gesture(string id) =>
        _overrides.TryGetValue(id, out var g) ? (g.Length == 0 ? null : g) : DefaultGesture(id);

    public bool IsDefault(string id) => !_overrides.ContainsKey(id);

    /// <summary>Sets a shortcut; null removes it. Setting it back to the default drops the override.</summary>
    public void Set(string id, string? gesture)
    {
        if (gesture == DefaultGesture(id)) _overrides.Remove(id);
        else _overrides[id] = gesture ?? "";
        Notify(raiseChanged: true);
    }

    public void Reset(string id)
    {
        _overrides.Remove(id);
        Notify(raiseChanged: true);
    }

    /// <summary>Other commands with the same shortcut.</summary>
    public IEnumerable<ShortcutCommand> Conflicts(string id) =>
        Gesture(id) is { } g ? Commands.Where(c => c.Id != id && Gesture(c.Id) == g) : [];

    public IReadOnlyList<KeyGesture> Parts(string id) =>
        Gesture(id) is { } g && TryParse(g, out var parts) ? parts : [];

    public static bool TryParse(string gesture, out IReadOnlyList<KeyGesture> parts)
    {
        try
        {
            parts = gesture.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(KeyGesture.Parse).ToList();
            return parts.Count is 1 or 2;
        }
        catch (ArgumentException)
        {
            parts = [];
            return false;
        }
    }

    /// <summary>"Alt+D1" reads as "Alt+1", and "Return" as "Enter".</summary>
    public static string Format(string? gesture) =>
        gesture is null || !TryParse(gesture, out var parts) ? "" : string.Join(" ", parts.Select(Format));

    public static string Format(KeyGesture g)
    {
        var text = g.ToString();
        if (g.Key == Key.Enter) return text[..^"Return".Length] + "Enter";
        return g.Key is >= Key.D0 and <= Key.D9 ? text[..^2] + (char)('0' + (g.Key - Key.D0)) : text;
    }

    /// <summary>The gesture string for a key press, or null for a lone modifier.</summary>
    public static string? FromKeyEvent(KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.None)
            return null;
        return new KeyGesture(e.Key, e.KeyModifiers).ToString();
    }

    private void Notify(bool raiseChanged)
    {
        Keys.Refresh();
        Labels.Refresh();
        Tips.Refresh();
        if (raiseChanged) Changed?.Invoke();
    }

    /// <summary>An indexer that bindings can watch: <c>{Binding Labels[Refresh], Source=…}</c>.</summary>
    public sealed class Lookup<T>(Func<string, T> get) : INotifyPropertyChanged
    {
        public T this[string id] => get(id);

        public event PropertyChangedEventHandler? PropertyChanged;

        internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
