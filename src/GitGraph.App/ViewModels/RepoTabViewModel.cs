using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Graph;

namespace GitGraph.App.ViewModels;

public sealed record BranchOption(string Label, string? RefName, bool IsRemote)
{
    public override string ToString() => Label;
}

/// <summary>The graph, history and diff view for one working tree.</summary>
public partial class RepoTabViewModel : TabViewModel
{
    private const int PageSize = 400;
    private const string UncommittedHash = "*uncommitted*";

    private readonly WorkspaceViewModel _workspace;

    // ---- Pull and push state, like the sync indicator in VS Code's status bar

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncCounts), nameof(SyncTooltip), nameof(ShowSync), nameof(CanPublish), nameof(IsInSync))]
    private SyncStatus? _sync;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncTooltip))]
    private DateTimeOffset? _lastFetched;

    [ObservableProperty]
    private bool _isSyncing;

    /// <summary>Why the last fetch failed; cleared by the next one that works.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncTooltip))]
    private string? _fetchError;

    public string SyncCounts => SyncText.Counts(Sync);
    public string SyncTooltip => SyncText.Describe(Sync, LastFetched, FetchError);
    public bool ShowSync => Sync is { HasRemote: true, Branch: not null };
    public bool CanPublish => Sync?.CanPublish == true;
    public bool IsInSync => ShowSync && !CanPublish && SyncCounts.Length == 0;

    private readonly GitRepository _repo;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _loadCts;
    private bool _loaded;
    private bool _suppressFilterReload;
    private int _maxCount = PageSize;
    private List<WorktreeInfo> _worktrees = [];
    private List<StashEntry> _stashes = [];
    private bool _rebuilding;
    // From the last refresh's status: untracked files, and when it ran.
    private IReadOnlyList<string> _untracked = [];
    private DateTime _lastRefresh;
    private string? _commonDir;

    public RepoTabViewModel(string path, WorkspaceViewModel workspace, IAppHost host) : base(path, workspace)
    {
        Host = host;
        _workspace = workspace;
        _repo = new GitRepository(path);
        _showRemotes = host.ShowRemoteBranches;
        Commands = new RepoCommands(host, path, RefreshAsync);
        Details = new DetailsViewModel(_repo, host) { RepoChanged = RefreshAsync, Commands = Commands };
        Stats = new RepoStatsViewModel(_repo);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RefreshWhenShownAsync();
        };
    }

    public IAppHost Host { get; }
    public DetailsViewModel Details { get; }

    /// <summary>The user's custom commands, run in this repository.</summary>
    public RepoCommands Commands { get; }

    /// <summary>The Repository menu's custom commands.</summary>
    public IReadOnlyList<CustomCommandItem> RepoCustomCommands =>
        Commands.For("Repository", () => new() { ["branch"] = _headBranch ?? "", ["sha"] = _headHash ?? "", ["short"] = _headHash?[..7] ?? "" });

    public string? HeadBranch => _headBranch;
    private string? _headHash;

    /// <summary>The graph's loads and refreshes, and the contributors view's; the details pane has its own.</summary>
    public PendingLoads Loads { get; } = new();

    /// <summary>Who contributed, shown in place of the graph while <see cref="ShowStats"/> is on.</summary>
    public RepoStatsViewModel Stats { get; }

    [ObservableProperty]
    private bool _showStats;

    private string? _statsHead;

    partial void OnShowStatsChanged(bool value)
    {
        if (value) _ = Loads.Track(Stats.EnsureLoadedAsync(_headBranch));
    }

    [RelayCommand]
    private void ToggleStats() => ShowStats = !ShowStats;

    /// <summary>Selects the checked-out commit, loading more history if it isn't on screen yet.</summary>
    [RelayCommand]
    private async Task GoToHead()
    {
        ShowStats = false;
        for (var page = 0; page < 10; page++)
        {
            if (Rows.FirstOrDefault(r => r.IsHead) is { } head)
            {
                SelectedRow = head;
                HeadFocused?.Invoke(head);
                return;
            }
            if (!HasMore) break;
            await LoadMore();
        }
        Host.ReportError("HEAD isn't in the loaded history. Choose \"All branches\" or the current branch to find it.");
    }

    /// <summary>Asks the view to bring HEAD's row into the middle of the list.</summary>
    public event Action<CommitRowViewModel>? HeadFocused;

    [ObservableProperty]
    private string? _branchLabel;

    partial void OnBranchLabelChanged(string? value) => Subtitle = value;

    [ObservableProperty]
    private ObservableCollection<CommitRowViewModel> _rows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    private CommitRowViewModel? _selectedRow;

    [ObservableProperty]
    private CommitRowViewModel? _compareRow;

    [ObservableProperty]
    private double _graphWidth = GraphCell.WidthFor(1);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private string? _loadError;

    [ObservableProperty]
    private bool _isEmpty;

    public ObservableCollection<BranchOption> BranchOptions { get; } = [];

    [ObservableProperty]
    private BranchOption? _selectedBranch;

    [ObservableProperty]
    private bool _showRemotes;

    [ObservableProperty]
    private string _findText = "";

    [ObservableProperty]
    private string? _findStatus;

    public bool CanCompare => SelectedRow != null;

    public override void EnsureLoaded()
    {
        if (_loaded)
        {
            // Shown again after changes it skipped while hidden.
            if (_stale) _ = RefreshAsync();
            return;
        }
        _loaded = true;
        _ = Loads.Track(InitAsync());
    }

    // Changes noticed while the tab wasn't on screen; it refreshes when shown.
    private bool _stale;

    private bool IsOnScreen => _workspace.IsCurrent && _workspace.SelectedTab == this;

    /// <summary>
    /// Refreshes a tab on screen; one that isn't (another tab or workspace is shown) only notes that it is out of
    /// date, so background tabs don't each reload their graph on every file change and fetch.
    /// </summary>
    public Task RefreshWhenShownAsync()
    {
        if (IsOnScreen) return RefreshAsync();
        _stale = true;
        return Task.CompletedTask;
    }

    // What the last rebuild showed; a refresh that reads the same skips rebuilding the rows.
    private string? _shownSignature;

    private async Task InitAsync()
    {
        try
        {
            var gitDir = await _repo.GetGitDirAsync();
            _commonDir = await _repo.GetCommonDirAsync();
            IsWorktree = gitDir != null && _commonDir != null &&
                         !WorkspaceViewModel.PathEquals(System.IO.Path.TrimEndingDirectorySeparator(gitDir), _commonDir);
            StartWatching(gitDir, _commonDir);
        }
        catch (Exception)
        {
            // Not fatal: the tab still works, it just will not refresh on its own.
        }
        await RefreshAsync();
    }

    partial void OnSelectedRowChanged(CommitRowViewModel? value)
    {
        // While the rows are replaced, the list drops its selection for a moment; showing that would blank the details.
        if (_rebuilding) return;
        if (CompareRow != null && value == CompareRow) ClearCompare();
        ShowDetails();
    }

    partial void OnSelectedBranchChanged(BranchOption? value)
    {
        if (_suppressFilterReload) return;
        _maxCount = PageSize;
        _ = RefreshAsync();
    }

    partial void OnShowRemotesChanged(bool value)
    {
        _maxCount = PageSize;
        _ = RefreshAsync();
    }

    partial void OnFindTextChanged(string value) => ApplyFind(jump: false);

    // ---- Loading -----------------------------------------------------------------------------

    [RelayCommand]
    public Task RefreshAsync() => Loads.Track(LoadRowsAsync());

    private async Task LoadRowsAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var ct = cts.Token;
        _stale = false;
        IsLoading = true;
        try
        {
            var filter = SelectedBranch?.RefName is { } refName ? new[] { refName } : null;
            var headTask = _repo.GetHeadAsync(ct);
            var refsTask = _repo.GetRefsAsync(ct);
            var wtTask = _repo.GetWorktreesAsync(ct);
            var countTask = _repo.GetUncommittedAsync(ct);
            var commitsTask = _repo.GetCommitsAsync(_maxCount, filter, ShowRemotes, CurrentFilter(), ct);
            var syncTask = _repo.GetSyncStatusAsync(ct);
            var stashTask = _repo.GetStashesAsync(ct);
            var opTask = _repo.GetOperationInProgressAsync(ct);
            await Task.WhenAll(headTask, refsTask, wtTask, countTask, commitsTask, syncTask, stashTask, opTask);
            if (ct.IsCancellationRequested) return;

            var head = headTask.Result;
            _worktrees = wtTask.Result;
            _headBranch = head.Branch;
            _headHash = head.Hash;
            OnPropertyChanged(nameof(RepoCustomCommands));
            Sync = syncTask.Result;
            // Contributor numbers only change when HEAD moves; reading them is a full log, so not on every refresh.
            if (head.Hash != _statsHead)
            {
                _statsHead = head.Hash;
                Stats.Invalidate();
                if (ShowStats) _ = Loads.Track(Stats.EnsureLoadedAsync(head.Branch));
            }
            _workspace.UpdateSync(Path, Sync);
            BranchLabel = head.Branch ?? (head.Hash is { } h ? $"detached at {h[..7]}" : "no commits yet");
            UpdateBranchOptions(refsTask.Result);
            _stashes = stashTask.Result;
            Operation = opTask.Result;
            // Most refreshes (a file saved, the window focused, a fetch that found nothing) read exactly what is shown.
            var (uncommitted, untracked) = countTask.Result;
            _untracked = untracked;
            _lastRefresh = DateTime.UtcNow;
            var signature = Signature(commitsTask.Result, refsTask.Result, head, uncommitted);
            if (signature == _shownSignature && Rows.Count > 0)
            {
                // The working tree can change without changing the count; the details check their own list.
                if (SelectedRow is { IsUncommitted: true }) ShowDetails(fromRefresh: true);
                LoadError = null;
                return;
            }
            _shownSignature = signature;
            Build(commitsTask.Result, refsTask.Result, head, uncommitted);
            if (_showConflicts)
            {
                _showConflicts = false;
                ShowConflicts();
            }
            LoadError = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is GitException or IOException or System.ComponentModel.Win32Exception)
        {
            if (!ct.IsCancellationRequested) LoadError = e.Message;
        }
        finally
        {
            if (_loadCts == cts) IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        _maxCount += PageSize;
        await RefreshAsync();
    }

    /// <summary>Everything the rows are built from, as one string to compare with the last build.</summary>
    private string Signature(List<CommitInfo> commits, List<GitRef> refs, HeadInfo head, int uncommitted)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(head.Hash).Append('|').Append(head.Branch).Append('|').Append(uncommitted).Append('|').Append(ShowRemotes)
            .Append('|').Append(CurrentFilter()).Append('\n');
        foreach (var c in commits) sb.Append(c.Hash).Append(' ').AppendJoin(',', c.Parents).Append('\n');
        foreach (var r in refs) sb.Append(r.FullName).Append(' ').Append(r.Target).Append(' ').Append(r.Upstream).Append(r.IsHead).Append('\n');
        foreach (var st in _stashes) sb.Append(st.Name).Append(' ').Append(st.Hash).Append('\n');
        foreach (var w in _worktrees) sb.Append(w.Path).Append(' ').Append(w.Head).Append('\n');
        return sb.ToString();
    }

    private void Build(List<CommitInfo> commits, List<GitRef> refs, HeadInfo head, int uncommitted)
    {
        // Each stash sits just above the commit it was made on, on a dashed line of its own, as in vscode-git-graph.
        // A stash whose commit isn't loaded yet appears once scrolling reaches it.
        // A filtered list shows matching commits only, so no stashes.
        var commitFilter = CurrentFilter();
        var stashesByBase = (commitFilter is null ? _stashes : []).ToLookup(st => st.BaseHash);
        var entries = new List<(CommitInfo Commit, StashEntry? Stash)>(commits.Count + _stashes.Count);
        foreach (var c in commits)
        {
            foreach (var st in stashesByBase[c.Hash])
                entries.Add((new CommitInfo(st.Hash, [st.BaseHash], st.AuthorName, st.AuthorEmail, st.Date, st.Message), st));
            entries.Add((c, null));
        }

        var inputs = new List<GraphInput>(entries.Count + 1);
        var hasUncommitted = uncommitted > 0;
        // Filtered out, HEAD has no row for the uncommitted changes to hang from.
        if (hasUncommitted)
            inputs.Add(new GraphInput(UncommittedHash,
                head.Hash is { } hh && (commitFilter is null || commits.Any(c => c.Hash == hh)) ? [hh] : [], Dashed: true));
        // Filtering by author or date leaves parents out of the list; lines to them would lead nowhere, so they go.
        var shown = commitFilter is { DropsParents: true } ? commits.Select(c => c.Hash).ToHashSet() : null;
        inputs.AddRange(entries.Select(e => new GraphInput(e.Commit.Hash,
            shown is null ? e.Commit.Parents : e.Commit.Parents.Where(shown.Contains).ToList(), Dashed: e.Stash != null)));
        var graph = GraphLayout.Build(inputs);

        var refsByTarget = refs.Where(r => ShowRemotes || r.Kind != RefKind.RemoteBranch).ToLookup(r => r.Target);
        var otherWorktrees = _worktrees
            .Where(w => !WorkspaceViewModel.PathEquals(w.Path, Path) && w.Head != null && !w.IsBare)
            .ToLookup(w => w.Head!);

        var rows = new ObservableCollection<CommitRowViewModel>();
        var maxLanes = 1;
        var g = 0;
        if (hasUncommitted)
        {
            var info = new CommitInfo(UncommittedHash, head.Hash is { } hh ? [hh] : [], "", "", DateTimeOffset.Now,
                $"Uncommitted changes ({uncommitted})");
            rows.Add(new CommitRowViewModel(this, info, graph[g++], false, true));
        }
        foreach (var (c, stash) in entries)
        {
            var gr = graph[g++];
            maxLanes = Math.Max(maxLanes, gr.LaneCount);
            if (stash != null)
            {
                var stashRow = new CommitRowViewModel(this, c, gr, false, false) { Stash = stash };
                stashRow.Badges.Add(new RefBadgeViewModel(this, BadgeKind.Stash, stash.Name, gr.NodeColor, null, false));
                rows.Add(stashRow);
                continue;
            }
            var row = new CommitRowViewModel(this, c, gr, c.Hash == head.Hash, false);
            // As in vscode-git-graph, a local branch and its remote copies on the same commit share one label
            // ("main │ origin"); remote branches without a local one get their own.
            var atCommit = refsByTarget[c.Hash].OrderBy(r => r.Kind).ThenByDescending(r => r.IsHead).ToList();
            var merged = new HashSet<GitRef>();
            foreach (var r in atCommit)
            {
                if (merged.Contains(r)) continue;
                var kind = r.Kind switch { RefKind.LocalBranch => BadgeKind.Local, RefKind.RemoteBranch => BadgeKind.Remote, _ => BadgeKind.Tag };
                var badge = new RefBadgeViewModel(this, kind, r.Name, gr.NodeColor, r, r.IsHead && r.Name == head.Branch);
                if (kind == BadgeKind.Local)
                    foreach (var remote in atCommit.Where(x => x.Kind == RefKind.RemoteBranch && x.Remote is { } rn && x.Name == $"{rn}/{r.Name}"))
                    {
                        badge.Remotes.Add(remote.Remote!);
                        merged.Add(remote);
                    }
                row.Badges.Add(badge);
            }
            foreach (var w in otherWorktrees[c.Hash])
                row.Badges.Add(new RefBadgeViewModel(this, BadgeKind.Worktree, System.IO.Path.GetFileName(w.Path), gr.NodeColor, null, false));
            rows.Add(row);
        }

        var selectedHash = SelectedRow?.Hash;
        var compareHash = CompareRow?.Hash;
        // At least wide enough for the "Graph" column heading.
        GraphWidth = Math.Max(GraphCell.WidthFor(Math.Min(maxLanes, 24)), 52);
        _rebuilding = true;
        try
        {
            Rows = rows;
            HasMore = commits.Count >= _maxCount;
            IsEmpty = rows.Count == 0;

            CompareRow = compareHash is null ? null : rows.FirstOrDefault(r => r.Hash == compareHash);
            if (CompareRow != null) CompareRow.IsCompareTarget = true;
            var reselect = selectedHash is null ? null : rows.FirstOrDefault(r => r.Hash == selectedHash);
            if (reselect != null)
                SelectedRow = reselect;
            else if (selectedHash == UncommittedHash)
                // Everything was committed (show the new commit) or discarded.
                SelectedRow = rows.FirstOrDefault(r => r.Hash == head.Hash);
            else
                SelectedRow = null;
        }
        finally { _rebuilding = false; }
        // Once, for the row selected after the rebuild; the same commit as before keeps its details as they are.
        ShowDetails(fromRefresh: true);
        ApplyFind(jump: false);
        _refs = refs;
        ApplyHighlight();
    }

    private void UpdateBranchOptions(List<GitRef> refs)
    {
        var current = SelectedBranch?.RefName;
        var options = new List<BranchOption> { new("All branches", null, false) };
        options.AddRange(refs.Where(r => r.Kind == RefKind.LocalBranch).Select(r => new BranchOption(r.Name, r.FullName, false)));
        if (ShowRemotes)
            options.AddRange(refs.Where(r => r.Kind == RefKind.RemoteBranch).Select(r => new BranchOption(r.Name, r.FullName, true)));

        if (options.Select(o => o.RefName).SequenceEqual(BranchOptions.Select(o => o.RefName))) return;
        _suppressFilterReload = true;
        BranchOptions.Clear();
        foreach (var o in options) BranchOptions.Add(o);
        SelectedBranch = options.FirstOrDefault(o => o.RefName == current) ?? options[0];
        _suppressFilterReload = false;
    }

    // ---- Selection, compare, find --------------------------------------------------------------

    /// <param name="fromRefresh">
    /// Right after a refresh, its list of untracked files is current and saves the details finding them again. When
    /// the user picks the row, the list may be old (new files don't touch .git, so nothing refreshed), so they look.
    /// </param>
    private void ShowDetails(bool fromRefresh = false)
    {
        var row = SelectedRow;
        if (row is null)
        {
            Details.Clear();
            return;
        }
        if (CompareRow != null && CompareRow != row)
        {
            // Older commit on the left, so the diff reads as "what changed going forward".
            var (older, newer) = Rows.IndexOf(CompareRow) > Rows.IndexOf(row) ? (CompareRow, row) : (row, CompareRow);
            Details.ShowCompare(older.Commit, newer.IsUncommitted ? null : newer.Commit);
            return;
        }
        if (row.IsUncommitted) Details.ShowUncommitted(row.Commit.Parents.Count > 0 ? "HEAD" : GitRepository.EmptyTree, fromRefresh ? _untracked : null);
        else if (row.Stash is { } stash) Details.ShowStash(stash);
        else Details.ShowCommit(row.Commit);
    }

    public void SetCompare(CommitRowViewModel row)
    {
        if (SelectedRow is null || SelectedRow == row)
        {
            SelectedRow = row;
            return;
        }
        if (CompareRow != null) CompareRow.IsCompareTarget = false;
        CompareRow = row;
        row.IsCompareTarget = true;
        ShowDetails();
    }

    [RelayCommand]
    public void ClearCompare()
    {
        if (CompareRow is null) return;
        CompareRow.IsCompareTarget = false;
        CompareRow = null;
        ShowDetails();
    }

    private void ApplyFind(bool jump)
    {
        // Filters typed in the box apply on Enter; until then nothing is matched against them.
        if (FindQuery.Parse(FindText) is { HasFilters: true } or { Problem: not null })
        {
            foreach (var r in Rows) r.IsMatch = false;
            FindStatus = "Enter to filter";
            return;
        }
        var text = FindText.Trim();
        var matches = 0;
        foreach (var r in Rows)
        {
            r.IsMatch = text.Length > 0 && !r.IsUncommitted && Matches(r, text);
            if (r.IsMatch) matches++;
        }
        FindStatus = text.Length == 0 ? null : matches == 0 ? "No matches" : matches == 1 ? "1 match" : $"{matches} matches";
        if (jump) FindNext();
    }

    private static bool Matches(CommitRowViewModel r, string text) =>
        r.Subject.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        r.Author.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        r.Hash.StartsWith(text, StringComparison.OrdinalIgnoreCase) ||
        r.Badges.Any(b => b.Text.Contains(text, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    public void FindNext() => StepFind(+1);

    [RelayCommand]
    public void FindPrevious() => StepFind(-1);

    private void StepFind(int dir)
    {
        if (Rows.Count == 0) return;
        var start = SelectedRow is null ? (dir > 0 ? -1 : Rows.Count) : Rows.IndexOf(SelectedRow);
        for (var i = 1; i <= Rows.Count; i++)
        {
            var idx = ((start + dir * i) % Rows.Count + Rows.Count) % Rows.Count;
            if (Rows[idx].IsMatch)
            {
                SelectedRow = Rows[idx];
                return;
            }
        }
    }

    // ---- File watching -----------------------------------------------------------------------

    private void StartWatching(params string?[] dirs)
    {
        foreach (var dir in dirs.Where(d => d != null).Distinct())
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var w = new FileSystemWatcher(dir!)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                };
                w.Changed += OnGitDirChanged;
                w.Created += OnGitDirChanged;
                w.Deleted += OnGitDirChanged;
                w.Renamed += OnGitDirChanged;
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception e) when (e is IOException or ArgumentException or PlatformNotSupportedException)
            {
                // Watching is a convenience; manual refresh still works.
            }
        }
    }

    private void OnGitDirChanged(object sender, FileSystemEventArgs e)
    {
        var name = e.Name ?? "";
        if (name.StartsWith("objects", StringComparison.Ordinal) || name.StartsWith("logs", StringComparison.Ordinal) ||
            name.EndsWith(".lock", StringComparison.Ordinal) || name.Contains("fsmonitor", StringComparison.Ordinal))
            return;
        Dispatcher.UIThread.Post(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });
    }

    /// <summary>
    /// Cheap refresh for when the window regains focus: files may have been edited elsewhere. Switching back and
    /// forth quickly doesn't refresh each time.
    /// </summary>
    public override void OnActivated()
    {
        if (_loaded && DateTime.UtcNow - _lastRefresh > TimeSpan.FromSeconds(3)) _ = RefreshAsync();
    }

    public override void Dispose()
    {
        _loadCts?.Cancel();
        _debounce.Stop();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        Details.Clear();
    }
}
