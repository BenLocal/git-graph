using CommunityToolkit.Mvvm.ComponentModel;

namespace GitGraph.App.ViewModels;

/// <summary>An open tab: a repository's history (<see cref="RepoTabViewModel"/>) or a folder's files (<see cref="FolderTabViewModel"/>).</summary>
public abstract partial class TabViewModel : ObservableObject, IDisposable
{
    protected TabViewModel(string path, WorkspaceViewModel workspace)
    {
        Path = path;
        Workspace = workspace;
        Title = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
    }

    public string Path { get; }
    public string Title { get; }

    /// <summary>The workspace this tab belongs to, for the tab's own context menu.</summary>
    public WorkspaceViewModel Workspace { get; }

    /// <summary>
    /// Opened by a single click: shown in italics and replaced by the next single-click open,
    /// until a double-click (or "Keep open") pins it, as in VS Code.
    /// </summary>
    [ObservableProperty]
    private bool _isPreview;

    /// <summary>Shown muted after the title: the branch of a repository.</summary>
    [ObservableProperty]
    private string? _subtitle;

    [ObservableProperty]
    private bool _isWorktree;

    public virtual bool IsFolder => false;

    /// <summary>How the tab is saved in the workspace; a folder and a repository at the same path are different tabs.</summary>
    public virtual string StateKey => Path;

    public virtual void EnsureLoaded() { }

    /// <summary>The window came back to the front; files may have changed meanwhile.</summary>
    public virtual void OnActivated() { }

    public abstract void Dispose();
}
