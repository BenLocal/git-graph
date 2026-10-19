using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitGraph.Core.Workspaces;

public sealed class WorkspaceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Workspace";
    /// <summary>Folders searched for repositories. A folder may itself be a repository.</summary>
    public List<string> Folders { get; set; } = [];
    /// <summary>Working-tree paths open as tabs, in tab order.</summary>
    public List<string> OpenTabs { get; set; } = [];
    public string? ActiveTab { get; set; }
    /// <summary>The tab opened by a single click, replaced by the next one until it is kept open.</summary>
    public string? PreviewTab { get; set; }
    /// <summary>Sidebar folders whose worktree list is folded away.</summary>
    public List<string> CollapsedFolders { get; set; } = [];
}

public sealed class AppState
{
    public List<WorkspaceConfig> Workspaces { get; set; } = [];
    public string? ActiveWorkspaceId { get; set; }
    /// <summary>"Dark", "Light" or "System".</summary>
    public string Theme { get; set; } = "System";
    public bool ShowRemoteBranches { get; set; } = true;
    public bool SplitDiff { get; set; }
    /// <summary>Diffs treat lines that differ only in whitespace as unchanged.</summary>
    public bool IgnoreWhitespace { get; set; }
    /// <summary>Long diff lines wrap; otherwise the diff scrolls sideways.</summary>
    public bool WrapDiffLines { get; set; } = true;
    /// <summary>View → Font size: a factor for all text, 1 for the default sizes.</summary>
    public double FontScale { get; set; } = 1;
    /// <summary>Whether the commit details panel under the graph is open.</summary>
    public bool ShowDetailsPanel { get; set; } = true;
    public bool ShowSidebar { get; set; } = true;
    /// <summary>"Robots": GitHub pictures and Gravatar, with robots for people without one. "Initials": drawn locally, nothing sent.</summary>
    public string AvatarStyle { get; set; } = "Robots";
    /// <summary>Fetch every repository in the open workspace every few minutes, like VS Code's git.autofetch.</summary>
    public bool AutoFetch { get; set; } = true;
    /// <summary>Minutes between automatic fetches.</summary>
    public int AutoFetchMinutes { get; set; } = 3;
    /// <summary>How Pull brings in new commits: "GitConfig", "Merge" or "Rebase".</summary>
    public string PullMode { get; set; } = "GitConfig";
    /// <summary>Keyboard shortcuts the user changed, by command id; an empty value removes a shortcut.</summary>
    public Dictionary<string, string> Keybindings { get; set; } = [];
    /// <summary>Each folder tab's open subfolders and selected file, by folder path, so it reopens as it was left.</summary>
    public Dictionary<string, FolderViewState> FolderViews { get; set; } = [];
    /// <summary>Show a chain of single-subfolder folders (src/main/java/com/example) as one row, like VS Code's explorer.compactFolders.</summary>
    public bool CompactFolders { get; set; } = true;
}

public sealed class FolderViewState
{
    /// <summary>Open subfolders, relative to the folder, with "/" separators.</summary>
    public List<string> Expanded { get; set; } = [];
    public string? Selected { get; set; }
    /// <summary>The tree's width after the user dragged the divider; null for the default.</summary>
    public double? TreeWidth { get; set; }
}

[JsonSerializable(typeof(AppState))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppStateJson : JsonSerializerContext;

public sealed class WorkspaceStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitGraph", "state.json");

    public AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(FilePath), AppStateJson.Default.AppState);
                if (s != null) return Normalize(s);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged state file must not stop the app from starting; fall back to a fresh one.
        }
        return Normalize(new AppState());
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, AppStateJson.Default.AppState));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static AppState Normalize(AppState s)
    {
        if (s.Workspaces.Count == 0) s.Workspaces.Add(new WorkspaceConfig { Name = "Default" });
        if (s.ActiveWorkspaceId is null || s.Workspaces.All(w => w.Id != s.ActiveWorkspaceId))
            s.ActiveWorkspaceId = s.Workspaces[0].Id;
        return s;
    }
}
