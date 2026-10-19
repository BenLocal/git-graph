using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.ViewModels;

public partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly IAppHost _host;
    private bool _scanned;
    private bool _tabsRestored;
    private CancellationTokenSource? _scanCts;

    public WorkspaceViewModel(WorkspaceConfig config, IAppHost host)
    {
        Config = config;
        _host = host;
        _name = config.Name;
        foreach (var f in config.Folders)
        {
            Folders.Add(f);
            FolderNodes.Add(new FolderNodeViewModel(f, this));
        }
        Tabs.CollectionChanged += (_, _) => CloseTabsToRightCommand.NotifyCanExecuteChanged();
    }

    public WorkspaceConfig Config { get; }
    internal IAppHost Host => _host;

    [ObservableProperty]
    private string _name;

    partial void OnNameChanged(string value) => Config.Name = value;

    public ObservableCollection<string> Folders { get; } = [];
    public ObservableCollection<RepoNodeViewModel> Repos { get; } = [];
    /// <summary>The sidebar's folder rows; selecting one opens a folder tab.</summary>
    public ObservableCollection<FolderNodeViewModel> FolderNodes { get; } = [];

    /// <summary>
    /// Puts each linked worktree under the workspace folder that contains it (the innermost one), so the
    /// Folders list reads as a tree. Worktrees outside every folder aren't listed there; their rows under
    /// Repositories can browse their files or open them in VS Code.
    /// </summary>
    internal void RebuildWorktreeFolders()
    {
        var active = SelectedTab is FolderTabViewModel f ? f.Path : null;
        foreach (var folder in FolderNodes) folder.Worktrees.Clear();
        var sep = System.IO.Path.DirectorySeparatorChar;
        foreach (var r in Repos)
            foreach (var w in r.Worktrees.Where(w => !w.IsPrunable))
            {
                var node = new FolderNodeViewModel(w.Path, this, w.Branch) { IsActive = active != null && PathEquals(active, w.Path) };
                var owner = FolderNodes
                    .Where(fn => w.Path.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(fn.Path) + sep, StringComparison.Ordinal))
                    .MaxBy(fn => fn.Path.Length);
                owner?.Worktrees.Add(node);
            }
        foreach (var folder in FolderNodes) folder.NotifyWorktreesChanged();
    }

    internal bool IsFolderCollapsed(string path) => Config.CollapsedFolders.Any(p => PathEquals(p, path));

    internal void SetFolderCollapsed(string path, bool collapsed)
    {
        Config.CollapsedFolders.RemoveAll(p => PathEquals(p, path));
        if (collapsed) Config.CollapsedFolders.Add(path);
        _host.SaveState();
    }
    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTabs), nameof(SelectedRepoTab), nameof(SelectedFolderTab))]
    private TabViewModel? _selectedTab;

    /// <summary>The selected tab when it shows a repository; menus and shortcuts for git act on it.</summary>
    public RepoTabViewModel? SelectedRepoTab => SelectedTab as RepoTabViewModel;
    public FolderTabViewModel? SelectedFolderTab => SelectedTab as FolderTabViewModel;

    [ObservableProperty]
    private bool _isScanning;

    public bool HasFolders => Folders.Count > 0;
    public bool HasTabs => SelectedTab != null;
    public bool HasNoRepos => !IsScanning && Repos.Count == 0 && Folders.Count > 0;

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(HasNoRepos));

    /// <summary>The workspace on screen; tabs of the others put off refreshing until they are shown.</summary>
    public bool IsCurrent { get; set; }

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        Config.ActiveTab = value?.StateKey;
        var repoPath = value is RepoTabViewModel ? value.Path : null;
        foreach (var r in Repos) r.MarkActive(repoPath);
        foreach (var f in FolderNodes.Concat(FolderNodes.SelectMany(n => n.Worktrees)))
            f.IsActive = value is FolderTabViewModel && PathEquals(f.Path, value.Path);
        value?.EnsureLoaded();
        if (_tabsRestored) _host.SaveState();
    }

    public void EnsureScanned()
    {
        if (_scanned) return;
        _scanned = true;
        _ = ScanAsync();
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        IsScanning = true;
        try
        {
            var found = await RepoDiscovery.DiscoverAsync(Folders.ToList(), cts.Token);
            if (cts.IsCancellationRequested) return;
            var expanded = Repos.Where(r => r.IsExpanded).Select(r => r.MainPath).ToHashSet();
            Repos.Clear();
            foreach (var r in found)
                Repos.Add(new RepoNodeViewModel(r, this) { IsExpanded = expanded.Count == 0 || expanded.Contains(r.MainPath) });
            RebuildWorktreeFolders();
            RestoreTabs();
            _ = LoadSyncAsync();
            foreach (var r in Repos) r.MarkActive(SelectedTab?.Path);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            _host.ReportError($"Could not scan the workspace folders: {e.Message}");
        }
        finally
        {
            if (_scanCts == cts) IsScanning = false;
            OnPropertyChanged(nameof(HasNoRepos));
        }
    }

    private void RestoreTabs()
    {
        if (_tabsRestored) return;
        _tabsRestored = true;
        foreach (var key in Config.OpenTabs)
        {
            TabViewModel tab = key.StartsWith(FolderPrefix, StringComparison.Ordinal)
                ? new FolderTabViewModel(key[FolderPrefix.Length..], this, _host)
                : new RepoTabViewModel(key, this, _host);
            if (!Directory.Exists(tab.Path)) continue;
            tab.IsPreview = tab.StateKey == Config.PreviewTab;
            Tabs.Add(tab);
        }
        SelectedTab = Tabs.FirstOrDefault(t => t.StateKey == Config.ActiveTab) ?? Tabs.FirstOrDefault();
        // Nothing open yet: show the first repository so the window is never empty when there is something to show.
        if (SelectedTab is null && Repos.Count > 0) OpenTab(Repos[0].MainPath);
    }

    /// <summary>
    /// Shows a repository. A single click opens it as the preview tab, taking the place of the
    /// previous preview; <paramref name="pin"/> (a double-click or an explicit action) keeps it open.
    /// </summary>
    public void OpenTab(string path, bool pin = false) =>
        Open(new RepoTabViewModel(path, this, _host), pin);

    /// <summary>Shows a folder's files; preview and pinning work as for repositories.</summary>
    public void OpenFolderTab(string path, bool pin = false) =>
        Open(new FolderTabViewModel(path, this, _host), pin);

    private const string FolderPrefix = "folder:";

    private void Open(TabViewModel candidate, bool pin)
    {
        var tab = Tabs.FirstOrDefault(t => t.GetType() == candidate.GetType() && PathEquals(t.Path, candidate.Path));
        if (tab != null) candidate.Dispose();
        if (tab is null)
        {
            tab = candidate;
            tab.IsPreview = !pin;
            var preview = pin ? null : Tabs.FirstOrDefault(t => t.IsPreview);
            if (preview != null)
            {
                var index = Tabs.IndexOf(preview);
                preview.Dispose();
                Tabs[index] = tab;
            }
            else
                Tabs.Add(tab);
        }
        else if (pin)
            tab.IsPreview = false;
        SelectedTab = tab;
        PersistTabs();
    }

    [RelayCommand]
    private void PinTab(TabViewModel tab)
    {
        if (!tab.IsPreview) return;
        tab.IsPreview = false;
        PersistTabs();
    }

    [RelayCommand]
    private void CloseTab(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        tab.Dispose();
        Tabs.RemoveAt(index);
        if (SelectedTab == tab || SelectedTab is null)
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        PersistTabs();
    }

    [RelayCommand(CanExecute = nameof(HasTabsToRight))]
    private void CloseTabsToRight(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        CloseTabs(Tabs.Skip(index + 1).ToList(), keep: tab);
    }

    private bool HasTabsToRight(TabViewModel? tab) => tab != null && Tabs.IndexOf(tab) is var i && i >= 0 && i < Tabs.Count - 1;

    [RelayCommand]
    private void CloseAllTabs() => CloseTabs(Tabs.ToList(), keep: null);

    private void CloseTabs(List<TabViewModel> closing, TabViewModel? keep)
    {
        if (closing.Count == 0) return;
        foreach (var t in closing)
        {
            t.Dispose();
            Tabs.Remove(t);
        }
        // If the active tab was closed, activate the tab the menu was opened on, as VS Code does.
        if (SelectedTab is null || closing.Contains(SelectedTab)) SelectedTab = keep ?? Tabs.LastOrDefault();
        PersistTabs();
    }

    private void PersistTabs()
    {
        Config.OpenTabs = Tabs.Select(t => t.StateKey).ToList();
        Config.PreviewTab = Tabs.FirstOrDefault(t => t.IsPreview)?.StateKey;
        _host.SaveState();
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        var path = await _host.Dialogs.PickFolderAsync($"Add a folder to {Name}");
        if (path is null) return;
        path = System.IO.Path.TrimEndingDirectorySeparator(path);
        if (Folders.Any(f => PathEquals(f, path))) return;
        Folders.Add(path);
        FolderNodes.Add(new FolderNodeViewModel(path, this));
        Config.Folders = Folders.ToList();
        OnPropertyChanged(nameof(HasFolders));
        _host.SaveState();
        await ScanAsync();
    }

    [RelayCommand]
    private async Task RemoveFolder(string path)
    {
        Folders.Remove(path);
        if (FolderNodes.FirstOrDefault(f => f.Path == path) is { } node) FolderNodes.Remove(node);
        if (Tabs.FirstOrDefault(t => t is FolderTabViewModel && PathEquals(t.Path, path)) is { } open) CloseTab(open);
        Config.Folders = Folders.ToList();
        OnPropertyChanged(nameof(HasFolders));
        _host.SaveState();
        await ScanAsync();
    }

    /// <summary>Called after worktrees change so the sidebar shows the new set.</summary>
    public async Task RefreshRepoAsync(string anyWorktreePath)
    {
        var node = Repos.FirstOrDefault(r => r.Contains(anyWorktreePath));
        if (node != null) await node.ReloadWorktreesAsync();
        else await ScanAsync();
    }

    /// <summary>Shows a working tree's pull/push counts on its sidebar row.</summary>
    public void UpdateSync(string path, SyncStatus status)
    {
        foreach (var r in Repos)
        {
            if (PathEquals(r.MainPath, path)) r.Sync = status;
            foreach (var w in r.Worktrees)
                if (PathEquals(w.Path, path)) w.Sync = status;
        }
    }

    /// <summary>Commits to pull for each sidebar row, by path, to spot what a fetch brought in.</summary>
    private Dictionary<string, int> BehindCounts() =>
        Repos.SelectMany(r => new[] { (r.MainPath, r.Sync) }.Concat(r.Worktrees.Select(w => (w.Path, w.Sync))))
            .ToDictionary(x => x.Item1, x => x.Item2?.Behind ?? 0);

    /// <summary>Reads every repository's pull/push counts without touching the network.</summary>
    private async Task LoadSyncAsync()
    {
        foreach (var r in Repos.ToList())
            foreach (var path in new[] { r.MainPath }.Concat(r.Worktrees.Select(w => w.Path)))
            {
                try { UpdateSync(path, await new GitRepository(path).GetSyncStatusAsync()); }
                catch (Exception e) when (e is GitException or IOException or InvalidOperationException) { }
            }
    }

    private bool _fetching;

    /// <summary>
    /// The periodic fetch, like VS Code's git.autofetch: every repository in the workspace is fetched
    /// once (worktrees share refs), quietly, then the counts and open tabs catch up.
    /// </summary>
    public async Task AutoFetchAsync()
    {
        if (_fetching) return;
        _fetching = true;
        try
        {
            var before = BehindCounts();
            foreach (var r in Repos.ToList())
            {
                var tab = Tabs.OfType<RepoTabViewModel>().FirstOrDefault(t => r.Contains(t.Path));
                if (tab != null) r.FetchError = await tab.AutoFetchAsync();
                else
                {
                    try
                    {
                        await new GitRepository(r.MainPath).FetchAsync();
                        r.FetchError = null;
                    }
                    catch (GitException e) { r.FetchError = e.Message.Trim(); }
                }
                foreach (var t in Tabs.OfType<RepoTabViewModel>().Where(t => r.Contains(t.Path) && t != tab)) await t.RefreshWhenShownAsync();
            }
            await LoadSyncAsync();
            ReportNewCommits(before);
        }
        finally { _fetching = false; }
    }

    /// <summary>Marks the rows that have more to pull than before the fetch, and says so once in a note.</summary>
    private void ReportNewCommits(Dictionary<string, int> before)
    {
        var found = new List<string>();
        foreach (var r in Repos)
        {
            if (NewlyBehind(r.MainPath, r.Sync) is { } n)
            {
                // The open repository shows the new commits already; only the note mentions them.
                r.HasNewCommits = !r.IsActive;
                found.Add($"{r.Name} ({n} on {r.Sync!.Upstream})");
            }
            foreach (var w in r.Worktrees)
                if (NewlyBehind(w.Path, w.Sync) is { } m)
                {
                    w.HasNewCommits = !w.IsActive;
                    found.Add($"{w.Name} ({m} on {w.Sync!.Upstream})");
                }
        }
        if (found.Count > 0)
            _host.Notify($"New commits to pull in {string.Join(", ", found)}.");

        // A row with no count before the fetch was still loading; that isn't news.
        int? NewlyBehind(string path, SyncStatus? s) =>
            s is { Behind: > 0 } && before.TryGetValue(path, out var was) && s.Behind > was ? s.Behind - was : null;
    }

    internal static bool PathEquals(string a, string b) =>
        string.Equals(System.IO.Path.TrimEndingDirectorySeparator(a), System.IO.Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public void Dispose()
    {
        _scanCts?.Cancel();
        foreach (var t in Tabs) t.Dispose();
    }
}

public partial class RepoNodeViewModel : ObservableObject
{
    private readonly WorkspaceViewModel _workspace;

    public RepoNodeViewModel(DiscoveredRepo repo, WorkspaceViewModel workspace)
    {
        _workspace = workspace;
        Name = repo.Name;
        MainPath = repo.MainPath;
        SetWorktrees(repo.Worktrees);
    }

    public string Name { get; }
    public string MainPath { get; }
    public ObservableCollection<WorktreeNodeViewModel> Worktrees { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncCounts))]
    private string? _branch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncCounts), nameof(SyncTooltip))]
    private SyncStatus? _sync;

    /// <summary>Why the last periodic fetch of this repository failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncTooltip), nameof(HasFetchError))]
    private string? _fetchError;

    public bool HasFetchError => FetchError != null;

    /// <summary>The periodic fetch brought in commits to pull that nobody has looked at yet.</summary>
    [ObservableProperty]
    private bool _hasNewCommits;

    public string SyncCounts => SyncText.Counts(Sync);
    public string? SyncTooltip => Sync is null && FetchError is null ? null : SyncText.Describe(Sync, null, FetchError, clickable: false);

    partial void OnSyncChanged(SyncStatus? value)
    {
        if (value is not { Behind: > 0 }) HasNewCommits = false;
    }

    [ObservableProperty]
    private bool _isActive;

    partial void OnIsActiveChanged(bool value)
    {
        if (value) HasNewCommits = false;
    }

    public bool HasLinkedWorktrees => Worktrees.Count > 0;

    public bool Contains(string path) =>
        WorkspaceViewModel.PathEquals(MainPath, path) || Worktrees.Any(w => WorkspaceViewModel.PathEquals(w.Path, path));

    public void MarkActive(string? activePath)
    {
        IsActive = activePath != null && WorkspaceViewModel.PathEquals(MainPath, activePath);
        foreach (var w in Worktrees) w.IsActive = activePath != null && WorkspaceViewModel.PathEquals(w.Path, activePath);
    }

    private void SetWorktrees(IReadOnlyList<WorktreeInfo> wts)
    {
        Worktrees.Clear();
        var main = wts.FirstOrDefault(w => w.IsMain);
        Branch = main?.BranchShortName;
        foreach (var w in wts.Where(w => !w.IsMain && !w.IsBare))
            Worktrees.Add(new WorktreeNodeViewModel(w, this));
        OnPropertyChanged(nameof(HasLinkedWorktrees));
    }

    public async Task ReloadWorktreesAsync()
    {
        try
        {
            SetWorktrees(await new GitRepository(MainPath).GetWorktreesAsync());
            _workspace.RebuildWorktreeFolders();
        }
        catch (GitException e) { _workspace.Host.ReportError(e.Message); }
        MarkActive(_workspace.SelectedTab?.Path);
    }

    [RelayCommand]
    private void Open() => _workspace.OpenTab(MainPath);

    [RelayCommand]
    private void Pin() => _workspace.OpenTab(MainPath, pin: true);

    /// <summary>The repository's files in a folder tab, wherever it lives.</summary>
    [RelayCommand]
    private void BrowseFiles() => _workspace.OpenFolderTab(MainPath, pin: true);

    [RelayCommand]
    private void OpenInVsCode()
    {
        if (ExternalTools.OpenInVsCode(MainPath) is { } error) _workspace.Host.ReportError(error);
    }

    internal WorkspaceViewModel Workspace => _workspace;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private Task AddWorktree() => WorktreeActions.AddAsync(_workspace, MainPath, Name, "HEAD", null);

    [RelayCommand]
    private async Task PruneWorktrees()
    {
        try
        {
            await new GitRepository(MainPath).PruneWorktreesAsync();
            await ReloadWorktreesAsync();
        }
        catch (GitException e) { _workspace.Host.ReportError(e.Message); }
    }

    internal Task RemoveWorktreeAsync(WorktreeNodeViewModel wt) => WorktreeActions.RemoveAsync(_workspace, MainPath, wt.Path, wt.Name);

    internal void OpenPath(string path, bool pin = false) => _workspace.OpenTab(path, pin);
}

public partial class WorktreeNodeViewModel(WorktreeInfo info, RepoNodeViewModel repo) : ObservableObject
{
    public string Path { get; } = info.Path;
    public string Name { get; } = System.IO.Path.GetFileName(info.Path);
    public string Branch { get; } = info.BranchShortName;
    public bool IsLocked { get; } = info.IsLocked;
    public bool IsPrunable { get; } = info.IsPrunable;
    public string Tooltip => IsPrunable ? $"{Path}\nThis folder is gone. Prune worktrees to clean it up." : Path;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncCounts))]
    private SyncStatus? _sync;

    /// <summary>The periodic fetch brought in commits to pull that nobody has looked at yet.</summary>
    [ObservableProperty]
    private bool _hasNewCommits;

    public string SyncCounts => SyncText.Counts(Sync);

    partial void OnSyncChanged(SyncStatus? value)
    {
        if (value is not { Behind: > 0 }) HasNewCommits = false;
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (value) HasNewCommits = false;
    }

    [RelayCommand]
    private void Open() => repo.OpenPath(Path);

    [RelayCommand]
    private void Pin() => repo.OpenPath(Path, pin: true);

    /// <summary>The worktree's files in a folder tab, even when it lives outside the workspace folders.</summary>
    [RelayCommand]
    private void BrowseFiles() => repo.Workspace.OpenFolderTab(Path, pin: true);

    [RelayCommand]
    private void OpenInVsCode()
    {
        if (ExternalTools.OpenInVsCode(Path) is { } error) repo.Workspace.Host.ReportError(error);
    }

    [RelayCommand]
    private Task Remove() => repo.RemoveWorktreeAsync(this);
}

/// <summary>Worktree add/remove flows shared by the sidebar and the commit graph.</summary>
internal static class WorktreeActions
{
    public static async Task AddAsync(WorkspaceViewModel ws, string repoPath, string repoName, string commitish, string? suggestedBranch)
    {
        var parent = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(repoPath)) ?? repoPath;
        var suffix = suggestedBranch ?? (commitish.Length >= 7 && commitish != "HEAD" ? commitish[..7] : "worktree");
        var safe = string.Concat(suffix.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-'));
        var defaultPath = System.IO.Path.Combine(parent, $"{repoName}-{safe}");

        var isBranch = suggestedBranch != null;
        var r = await ws.Host.Dialogs.PromptAsync(
            "Add worktree",
            isBranch
                ? $"Check out {suggestedBranch} in a separate folder, so you can work on it without touching your current checkout."
                : $"Check out {(commitish == "HEAD" ? "the current commit" : commitish[..Math.Min(7, commitish.Length)])} in a separate folder.",
            [
                new PromptField("Folder", defaultPath),
                new PromptField("New branch", "",isBranch ? "Leave empty to use the existing branch" : "Leave empty for a detached checkout"),
            ],
            "Add worktree");
        if (r is null || string.IsNullOrWhiteSpace(r[0])) return;
        try
        {
            var newBranch = string.IsNullOrWhiteSpace(r[1]) ? null : r[1].Trim();
            await new GitRepository(repoPath).AddWorktreeAsync(r[0].Trim(), isBranch && newBranch is null ? suggestedBranch! : commitish,
                newBranch, detach: !isBranch && newBranch is null);
            await ws.RefreshRepoAsync(repoPath);
            ws.OpenTab(r[0].Trim(), pin: true);
        }
        catch (GitException e) { ws.Host.ReportError(e.Message); }
    }

    public static async Task RemoveAsync(WorkspaceViewModel ws, string repoPath, string wtPath, string name)
    {
        if (!await ws.Host.Dialogs.ConfirmAsync("Remove worktree",
                $"Remove the worktree \"{name}\" and delete its folder?\n{wtPath}\n\nIts branch stays in the repository.",
                "Remove worktree", destructive: true))
            return;
        var repo = new GitRepository(repoPath);
        try
        {
            await repo.RemoveWorktreeAsync(wtPath, force: false);
        }
        catch (GitException e) when (e.Message.Contains("modified or untracked", StringComparison.OrdinalIgnoreCase) ||
                                     e.Message.Contains("--force", StringComparison.Ordinal))
        {
            if (!await ws.Host.Dialogs.ConfirmAsync("Worktree has changes",
                    $"\"{name}\" has uncommitted changes. Removing it now discards them.", "Discard and remove", destructive: true))
                return;
            try { await repo.RemoveWorktreeAsync(wtPath, force: true); }
            catch (GitException e2) { ws.Host.ReportError(e2.Message); return; }
        }
        catch (GitException e) { ws.Host.ReportError(e.Message); return; }

        var tab = ws.Tabs.OfType<RepoTabViewModel>().FirstOrDefault(t => WorkspaceViewModel.PathEquals(t.Path, wtPath));
        if (tab != null) ws.CloseTabCommand.Execute(tab);
        await ws.RefreshRepoAsync(repoPath);
    }
}

/// <summary>A folder row in the sidebar: a single click previews its files, a double-click keeps the tab.</summary>
/// <param name="worktreeBranch">Set for a linked worktree listed among the folders; it can't be removed from there.</param>
public sealed partial class FolderNodeViewModel(string path, WorkspaceViewModel workspace, string? worktreeBranch = null) : ObservableObject
{
    public string Path { get; } = path;
    public bool IsWorktree { get; } = worktreeBranch != null;
    public bool IsFolder => !IsWorktree;
    public string? Branch { get; } = worktreeBranch;

    /// <summary>Linked worktrees inside this folder, shown nested under it.</summary>
    public ObservableCollection<FolderNodeViewModel> Worktrees { get; } = [];
    public bool HasWorktrees => Worktrees.Count > 0;

    [ObservableProperty]
    private bool _isExpanded = !workspace.IsFolderCollapsed(path);

    partial void OnIsExpandedChanged(bool value) => Workspace.SetFolderCollapsed(Path, !value);

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    internal void NotifyWorktreesChanged() => OnPropertyChanged(nameof(HasWorktrees));
    public string Name { get; } = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
    public WorkspaceViewModel Workspace { get; } = workspace;

    [ObservableProperty]
    private bool _isActive;

    [RelayCommand]
    private void Open() => Workspace.OpenFolderTab(Path);

    [RelayCommand]
    private void Pin() => Workspace.OpenFolderTab(Path, pin: true);

    [RelayCommand]
    private void OpenInVsCode()
    {
        if (ExternalTools.OpenInVsCode(Path) is { } error) Workspace.Host.ReportError(error);
    }

    [RelayCommand]
    private Task CopyPath() => Workspace.Host.Dialogs.CopyTextAsync(Path);

    [RelayCommand]
    private Task Remove() => Workspace.RemoveFolderCommand.ExecuteAsync(Path);
}
