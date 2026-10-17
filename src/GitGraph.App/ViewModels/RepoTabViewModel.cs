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

    public string SyncCounts => SyncText.Counts(Sync);
    public string SyncTooltip => SyncText.Describe(Sync, LastFetched);
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
    private string? _commonDir;

    public RepoTabViewModel(string path, WorkspaceViewModel workspace, IAppHost host) : base(path, workspace)
    {
        Host = host;
        _workspace = workspace;
        _repo = new GitRepository(path);
        _showRemotes = host.ShowRemoteBranches;
        Details = new DetailsViewModel(_repo, host);
        Stats = new RepoStatsViewModel(_repo);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RefreshAsync();
        };
    }

    public IAppHost Host { get; }
    public DetailsViewModel Details { get; }

    /// <summary>Who contributed, shown in place of the graph while <see cref="ShowStats"/> is on.</summary>
    public RepoStatsViewModel Stats { get; }

    [ObservableProperty]
    private bool _showStats;

    private string? _statsHead;

    partial void OnShowStatsChanged(bool value)
    {
        if (value) _ = Stats.EnsureLoadedAsync(_headBranch);
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
        if (_loaded) return;
        _loaded = true;
        _ = InitAsync();
    }

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
    public async Task RefreshAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var ct = cts.Token;
        IsLoading = true;
        try
        {
            var filter = SelectedBranch?.RefName is { } refName ? new[] { refName } : null;
            var headTask = _repo.GetHeadAsync(ct);
            var refsTask = _repo.GetRefsAsync(ct);
            var wtTask = _repo.GetWorktreesAsync(ct);
            var countTask = _repo.CountUncommittedAsync(ct);
            var commitsTask = _repo.GetCommitsAsync(_maxCount, filter, ShowRemotes, ct);
            var syncTask = _repo.GetSyncStatusAsync(ct);
            await Task.WhenAll(headTask, refsTask, wtTask, countTask, commitsTask, syncTask);
            if (ct.IsCancellationRequested) return;

            var head = headTask.Result;
            _worktrees = wtTask.Result;
            _headBranch = head.Branch;
            Sync = syncTask.Result;
            // Contributor numbers only change when HEAD moves; reading them is a full log, so not on every refresh.
            if (head.Hash != _statsHead)
            {
                _statsHead = head.Hash;
                Stats.Invalidate();
                if (ShowStats) _ = Stats.EnsureLoadedAsync(head.Branch);
            }
            _workspace.UpdateSync(Path, Sync);
            BranchLabel = head.Branch ?? (head.Hash is { } h ? $"detached at {h[..7]}" : "no commits yet");
            UpdateBranchOptions(refsTask.Result);
            Build(commitsTask.Result, refsTask.Result, head, countTask.Result);
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

    private void Build(List<CommitInfo> commits, List<GitRef> refs, HeadInfo head, int uncommitted)
    {
        var inputs = new List<GraphInput>(commits.Count + 1);
        var hasUncommitted = uncommitted > 0;
        if (hasUncommitted)
            inputs.Add(new GraphInput(UncommittedHash, head.Hash is { } hh ? [hh] : [], Dashed: true));
        inputs.AddRange(commits.Select(c => new GraphInput(c.Hash, c.Parents)));
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
        foreach (var c in commits)
        {
            var gr = graph[g++];
            maxLanes = Math.Max(maxLanes, gr.LaneCount);
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
        Rows = rows;
        HasMore = commits.Count >= _maxCount;
        IsEmpty = rows.Count == 0;

        CompareRow = compareHash is null ? null : rows.FirstOrDefault(r => r.Hash == compareHash);
        if (CompareRow != null) CompareRow.IsCompareTarget = true;
        var reselect = selectedHash is null ? null : rows.FirstOrDefault(r => r.Hash == selectedHash);
        if (reselect != null)
        {
            SelectedRow = reselect;
        }
        else if (selectedHash == UncommittedHash)
        {
            SelectedRow = null; // changes were committed or discarded
        }
        ApplyFind(jump: false);
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

    private void ShowDetails()
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
        if (row.IsUncommitted) Details.ShowUncommitted(row.Commit.Parents.Count > 0 ? "HEAD" : GitRepository.EmptyTree);
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

    // ---- Commands from rows and badges --------------------------------------------------------

    private string? _headBranch;

    /// <summary>What a merge or cherry-pick lands on, for dialog text.</summary>
    private string CurrentLabel => _headBranch ?? "the detached HEAD";

    public async Task MergeAsync(string target, string label)
    {
        var res = await Host.Dialogs.PromptAsync("Merge", $"Merge {label} into {CurrentLabel}.",
            [
                new PromptField("Always create a merge commit, even when a fast-forward is possible", "false", IsCheckbox: true),
                new PromptField("Squash the changes into the working tree without committing", "false", IsCheckbox: true),
            ],
            "Merge");
        if (res is null) return;
        await RunOperationAsync(GitOperation.Merge, () => _repo.MergeAsync(target, res[0] == "true", res[1] == "true"));
    }

    public async Task CherryPickAsync(CommitRowViewModel row)
    {
        if (row.IsUncommitted) return;
        var message = row.Commit.IsMerge
            ? $"Apply the changes of merge {row.ShortHash} \"{row.Subject}\" onto {CurrentLabel}, relative to its first parent."
            : $"Apply {row.ShortHash} \"{row.Subject}\" onto {CurrentLabel}.";
        var res = await Host.Dialogs.PromptAsync("Cherry-pick", message,
            [
                new PromptField("Add \"cherry picked from commit …\" to the message", "false", IsCheckbox: true),
                new PromptField("Apply the changes without committing", "false", IsCheckbox: true),
            ],
            "Cherry-pick");
        if (res is null) return;
        await RunOperationAsync(GitOperation.CherryPick,
            () => _repo.CherryPickAsync(row.Hash, row.Commit.IsMerge, res[0] == "true", res[1] == "true"));
    }

    /// <summary>Runs a merge or cherry-pick; if it stops on conflicts, offers to abort it.</summary>
    private async Task RunOperationAsync(GitOperation op, Func<Task> action)
    {
        var name = op == GitOperation.Merge ? "merge" : "cherry-pick";
        try
        {
            await action();
        }
        catch (GitException e)
        {
            if (await _repo.IsInProgressAsync(op))
            {
                var abort = await Host.Dialogs.ConfirmAsync($"The {name} stopped on conflicts",
                    $"Resolve the conflicted files in your editor and commit to finish, or abort the {name} to go back to where you were.\n\n{Trim(e.Message)}",
                    $"Abort {name}", destructive: true);
                if (abort)
                {
                    try { await _repo.AbortAsync(op); }
                    catch (GitException e2) { Host.ReportError(e2.Message); }
                }
            }
            else Host.ReportError(e.Message);
        }
        await RefreshAsync();
        await _workspace.RefreshRepoAsync(Path);
    }

    private static string Trim(string message)
    {
        var lines = message.Split('\n');
        return lines.Length <= 8 ? message : string.Join('\n', lines.Take(8)) + "\n…";
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (GitException e) { Host.ReportError(e.Message); }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Fetch()
    {
        await RunAsync(() => _repo.FetchAsync());
        LastFetched = DateTimeOffset.Now;
    }

    /// <summary>The periodic fetch: quiet, because a network hiccup isn't worth an error bar.</summary>
    public async Task AutoFetchAsync()
    {
        try { await _repo.FetchAsync(); }
        catch (GitException) { return; }
        LastFetched = DateTimeOffset.Now;
        await RefreshAsync();
    }

    /// <summary>The sync indicator: publish a branch with no upstream, otherwise pull then push.</summary>
    [RelayCommand]
    private async Task SyncNow()
    {
        if (Sync is not { Branch: { } branch } s || IsSyncing) return;
        if (s.CanPublish)
        {
            if (!await Host.Dialogs.ConfirmAsync("Publish branch", $"Push {branch} to {s.Remotes[0]} and track it there?", "Publish"))
                return;
            await RunSyncAsync(() => _repo.PublishAsync(s.Remotes[0], branch));
            return;
        }
        if (s.Upstream is null) return;
        if (!await Host.Dialogs.ConfirmAsync("Sync",
                $"Pull {(s.Behind > 0 ? $"{s.Behind} commit(s)" : "any new commits")} from {s.Upstream} into {branch}, then push {(s.Ahead > 0 ? $"{s.Ahead} commit(s)" : "anything new")}.",
                "Sync"))
            return;
        await RunSyncAsync(async () =>
        {
            await _repo.PullAsync();
            await _repo.PushAsync();
        });
    }

    [RelayCommand]
    private Task Pull() => RunSyncAsync(() => _repo.PullAsync());

    [RelayCommand]
    private Task Push() => Sync?.CanPublish == true && Sync.Branch is { } b
        ? RunSyncAsync(() => _repo.PublishAsync(Sync.Remotes[0], b))
        : RunSyncAsync(() => _repo.PushAsync());

    private async Task RunSyncAsync(Func<Task> action)
    {
        IsSyncing = true;
        try { await RunAsync(action); }
        finally { IsSyncing = false; }
        LastFetched = DateTimeOffset.Now;
        await _workspace.RefreshRepoAsync(Path);
    }

    public async Task CheckoutCommitAsync(CommitRowViewModel row)
    {
        if (row.IsUncommitted) return;
        if (!await Host.Dialogs.ConfirmAsync("Check out commit",
                $"Check out {row.ShortHash} in {Title}? You will be on a detached HEAD until you switch to a branch.", "Check out"))
            return;
        await RunAsync(() => _repo.CheckoutAsync(row.Hash));
        await _workspace.RefreshRepoAsync(Path);
    }

    public async Task CheckoutRefAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { } r) return;
        if (r.Kind == RefKind.RemoteBranch)
        {
            var localName = r.Name[(r.Remote!.Length + 1)..];
            var refs = await _repo.GetRefsAsync();
            await RunAsync(() => refs.Any(x => x.Kind == RefKind.LocalBranch && x.Name == localName)
                ? _repo.CheckoutAsync(localName)
                : GitRunner.RunAsync(Path, ["checkout", "--track", r.Name]));
        }
        else await RunAsync(() => _repo.CheckoutAsync(r.Name));
        await _workspace.RefreshRepoAsync(Path);
    }

    public async Task DeleteRefAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { } r) return;
        if (r.Kind == RefKind.Tag)
        {
            if (!await Host.Dialogs.ConfirmAsync("Delete tag", $"Delete the tag {r.Name}? Remote copies are not touched.", "Delete tag", true)) return;
            await RunAsync(() => _repo.DeleteTagAsync(r.Name));
            return;
        }
        if (!await Host.Dialogs.ConfirmAsync("Delete branch", $"Delete the branch {r.Name}?", "Delete branch", true)) return;
        try
        {
            await _repo.DeleteBranchAsync(r.Name, force: false);
        }
        catch (GitException e) when (e.Message.Contains("not fully merged", StringComparison.OrdinalIgnoreCase))
        {
            if (await Host.Dialogs.ConfirmAsync("Branch is not merged",
                    $"{r.Name} has commits that are not on any other branch. Deleting it may lose them.", "Delete anyway", true))
            {
                try { await _repo.DeleteBranchAsync(r.Name, force: true); }
                catch (GitException e2) { Host.ReportError(e2.Message); }
            }
        }
        catch (GitException e) { Host.ReportError(e.Message); }
        await RefreshAsync();
    }

    public async Task CreateBranchAsync(CommitRowViewModel row)
    {
        var res = await Host.Dialogs.PromptAsync("Create branch", $"Start a new branch at {row.ShortHash}.",
            [new PromptField("Branch name", "", "feature/my-change"), new PromptField("Check it out", "true", IsCheckbox: true)],
            "Create branch");
        if (res is null || string.IsNullOrWhiteSpace(res[0])) return;
        await RunAsync(() => _repo.CreateBranchAsync(res[0].Trim(), row.Hash, res[1] == "true"));
        await _workspace.RefreshRepoAsync(Path);
    }

    public async Task CreateTagAsync(CommitRowViewModel row)
    {
        var res = await Host.Dialogs.PromptAsync("Create tag", $"Tag {row.ShortHash}.",
            [new PromptField("Tag name", "", "v1.0.0"), new PromptField("Message", "", "Leave empty for a lightweight tag")],
            "Create tag");
        if (res is null || string.IsNullOrWhiteSpace(res[0])) return;
        await RunAsync(() => _repo.CreateTagAsync(res[0].Trim(), row.Hash, res[1]));
    }

    public async Task AddWorktreeAtAsync(CommitRowViewModel row, string? branch)
    {
        var main = _worktrees.FirstOrDefault(w => w.IsMain)?.Path ?? Path;
        await WorktreeActions.AddAsync(_workspace, main, System.IO.Path.GetFileName(main), row.IsUncommitted ? "HEAD" : row.Hash, branch);
        await RefreshAsync();
    }

    public async Task AddWorktreeForBranchAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { } r) return;
        var main = _worktrees.FirstOrDefault(w => w.IsMain)?.Path ?? Path;
        if (r.Kind == RefKind.RemoteBranch)
        {
            await WorktreeActions.AddAsync(_workspace, main, System.IO.Path.GetFileName(main), r.Name, null);
        }
        else await WorktreeActions.AddAsync(_workspace, main, System.IO.Path.GetFileName(main), r.Name, r.Name);
        await RefreshAsync();
    }

    public void OpenWorktreeByName(string name)
    {
        var wt = _worktrees.FirstOrDefault(w => System.IO.Path.GetFileName(w.Path) == name);
        if (wt != null) _workspace.OpenTab(wt.Path, pin: true);
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

    /// <summary>Cheap refresh for when the window regains focus: files may have been edited elsewhere.</summary>
    public override void OnActivated()
    {
        if (_loaded) _ = RefreshAsync();
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
