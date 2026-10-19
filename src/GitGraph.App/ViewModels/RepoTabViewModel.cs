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
    private string? _commonDir;

    public RepoTabViewModel(string path, WorkspaceViewModel workspace, IAppHost host) : base(path, workspace)
    {
        Host = host;
        _workspace = workspace;
        _repo = new GitRepository(path);
        _showRemotes = host.ShowRemoteBranches;
        Details = new DetailsViewModel(_repo, host) { RepoChanged = RefreshAsync };
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
            var commitsTask = _repo.GetCommitsAsync(_maxCount, filter, ShowRemotes, CurrentFilter(), ct);
            var syncTask = _repo.GetSyncStatusAsync(ct);
            var stashTask = _repo.GetStashesAsync(ct);
            var opTask = _repo.GetOperationInProgressAsync(ct);
            await Task.WhenAll(headTask, refsTask, wtTask, countTask, commitsTask, syncTask, stashTask, opTask);
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
            _stashes = stashTask.Result;
            Operation = opTask.Result;
            Build(commitsTask.Result, refsTask.Result, head, countTask.Result);
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
        ShowDetails();
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
        await RunOperationAsync(() => _repo.MergeAsync(target, res[0] == "true", res[1] == "true"));
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
        await RunOperationAsync(() => _repo.CherryPickAsync(row.Hash, row.Commit.IsMerge, res[0] == "true", res[1] == "true"));
    }

    public async Task RevertAsync(CommitRowViewModel row)
    {
        var message = row.Commit.IsMerge
            ? $"Undo merge {row.ShortHash} \"{row.Subject}\" on {CurrentLabel} with a new commit, relative to its first parent."
            : $"Undo {row.ShortHash} \"{row.Subject}\" on {CurrentLabel} with a new commit.";
        var res = await Host.Dialogs.PromptAsync("Revert", message,
            [new PromptField("Undo the changes without committing", "false", IsCheckbox: true)], "Revert");
        if (res is null) return;
        await RunOperationAsync(() => _repo.RevertAsync(row.Hash, row.Commit.IsMerge, res[0] == "true"));
    }

    public async Task ResetAsync(CommitRowViewModel row, ResetMode mode)
    {
        var (what, destructive) = mode switch
        {
            ResetMode.Soft => ("The commits after it stay as staged changes.", false),
            ResetMode.Mixed => ("The commits after it, and anything staged, stay as unstaged changes.", false),
            _ => ("The commits after it and all uncommitted changes to tracked files are thrown away. This can't be undone from here.", true),
        };
        if (!await Host.Dialogs.ConfirmAsync($"Reset ({mode.ToString().ToLowerInvariant()})",
                $"Move {CurrentLabel} to {row.ShortHash} \"{row.Subject}\". {what}", "Reset", destructive))
            return;
        await RunAsync(() => _repo.ResetAsync(row.Hash, mode));
    }

    public async Task RebaseAsync(string onto, string label)
    {
        var res = await Host.Dialogs.PromptAsync("Rebase",
            $"Replay the commits of {CurrentLabel} that aren't in {label} on top of it. This rewrites those commits: "
            + "if they were pushed already, pushing again needs a force push.",
            [new PromptField("Stash uncommitted changes first and bring them back afterwards", "true", IsCheckbox: true)], "Rebase");
        if (res is null) return;
        await RunOperationAsync(() => _repo.RebaseAsync(onto, res[0] == "true"));
    }

    /// <summary>
    /// Runs a merge, cherry-pick, revert or rebase. If it stops on conflicts, the banner above the graph offers to
    /// continue or abort, and the conflicted files show under the uncommitted changes.
    /// </summary>
    private async Task RunOperationAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (GitException e)
        {
            if (await _repo.GetOperationInProgressAsync() is null) Host.ReportError(e.Message);
            else _showConflicts = true;
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

    // ---- An operation stopped part-way ------------------------------------------------------

    /// <summary>A merge, cherry-pick, revert or rebase waiting to be continued or aborted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperation), nameof(OperationText))]
    private GitOperation? _operation;

    // After an operation stops, the uncommitted changes (with the conflicts) are selected once the graph reloads.
    private bool _showConflicts;

    public bool HasOperation => Operation != null;

    public string OperationText => Operation switch
    {
        null => "",
        var op => $"{OperationName(op.Value) switch { var n => char.ToUpperInvariant(n[0]) + n[1..] }} in progress. Resolve the conflicts "
            + "(under Merge conflicts in the uncommitted changes), stage the files, then continue. Or abort to go back to where you were.",
    };

    private static string OperationName(GitOperation op) => op switch
    {
        GitOperation.Merge => "merge",
        GitOperation.CherryPick => "cherry-pick",
        GitOperation.Revert => "revert",
        _ => "rebase",
    };

    [RelayCommand]
    private async Task ContinueOperation()
    {
        if (Operation is not { } op) return;
        await RunOperationAsync(() => _repo.ContinueAsync(op));
    }

    [RelayCommand]
    private async Task AbortOperation()
    {
        if (Operation is not { } op) return;
        var name = OperationName(op);
        if (!await Host.Dialogs.ConfirmAsync($"Abort {name}", $"Stop the {name} and go back to how things were before it started? "
                + "Conflict resolutions made so far are lost.", $"Abort {name}", destructive: true))
            return;
        await RunAsync(() => _repo.AbortAsync(op));
    }

    [RelayCommand]
    private void ShowConflicts()
    {
        if (Rows.FirstOrDefault(r => r.IsUncommitted) is not { } row) return;
        SelectedRow = row;
        Details.SelectedTabIndex = 1;
        if (!Host.ShowDetailsPanel) Host.ShowDetailsPanel = true;
    }

    [RelayCommand]
    private async Task Fetch()
    {
        if (await FetchQuietlyAsync() is { } error) Host.ReportError(error);
        await RefreshAsync();
    }

    /// <summary>The periodic fetch: a failure only shows in the sync tooltip, because a network hiccup isn't worth an error bar.</summary>
    public async Task<string?> AutoFetchAsync()
    {
        var error = await FetchQuietlyAsync();
        await RefreshAsync();
        return error;
    }

    private async Task<string?> FetchQuietlyAsync()
    {
        IsSyncing = true;
        try
        {
            await _repo.FetchAsync();
            LastFetched = DateTimeOffset.Now;
            FetchError = null;
        }
        catch (GitException e) { FetchError = Trim(e.Message); }
        finally { IsSyncing = false; }
        return FetchError;
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
            await _repo.PullAsync(Host.PullMode);
            await _repo.PushAsync();
        });
    }

    [RelayCommand]
    private Task Pull() => RunSyncAsync(() => _repo.PullAsync(Host.PullMode));

    [RelayCommand]
    private Task Push() => Sync?.CanPublish == true && Sync.Branch is { } b
        ? RunSyncAsync(() => _repo.PublishAsync(Sync.Remotes[0], b))
        : RunSyncAsync(() => _repo.PushAsync());

    /// <summary>Pushes over a rewritten history (after a rebase or amend), unless someone else pushed since the last fetch.</summary>
    [RelayCommand]
    private async Task ForcePush()
    {
        if (Sync is not { Branch: { } branch, Upstream: { } upstream }) return;
        if (!await Host.Dialogs.ConfirmAsync("Force push",
                $"Replace {upstream} with your {branch}? Commits on {upstream} that aren't in {branch} are dropped. "
                + "The push is refused if someone else pushed to it since your last fetch.",
                "Force push", destructive: true))
            return;
        await RunSyncAsync(() => _repo.PushAsync(forceWithLease: true));
    }

    private async Task RunSyncAsync(Func<Task> action)
    {
        IsSyncing = true;
        try { await RunAsync(action); }
        finally { IsSyncing = false; }
        LastFetched = DateTimeOffset.Now;
        await _workspace.RefreshRepoAsync(Path);
    }

    // ---- Filters -----------------------------------------------------------------------------

    public static IReadOnlyList<string> SinceOptions { get; } = ["Any time", "Last week", "Last month", "Last 3 months", "Last year"];

    /// <summary>The row of filter fields under the toolbar; hiding it clears the filters.</summary>
    [ObservableProperty]
    private bool _showFilters;

    [ObservableProperty]
    private string _authorFilter = "";

    [ObservableProperty]
    private string _pathFilter = "";

    [ObservableProperty]
    private string _sinceFilter = SinceOptions[0];

    private DispatcherTimer? _filterDebounce;

    public bool IsFiltered => CurrentFilter() != null;

    private CommitFilter? CurrentFilter()
    {
        DateTimeOffset? since = SinceFilter switch
        {
            "Last week" => DateTimeOffset.Now.AddDays(-7),
            "Last month" => DateTimeOffset.Now.AddMonths(-1),
            "Last 3 months" => DateTimeOffset.Now.AddMonths(-3),
            "Last year" => DateTimeOffset.Now.AddYears(-1),
            _ => null,
        };
        var filter = new CommitFilter(AuthorFilter.Trim(), since, PathFilter.Trim().Replace('\\', '/'));
        return filter.IsEmpty ? null : filter;
    }

    // Typing waits for a pause before reloading; picking a period reloads at once.
    partial void OnAuthorFilterChanged(string value) => ApplyFiltersSoon();
    partial void OnPathFilterChanged(string value) => ApplyFiltersSoon();
    partial void OnSinceFilterChanged(string value) => ApplyFilters();

    partial void OnShowFiltersChanged(bool value)
    {
        if (value || !IsFiltered) return;
        _authorFilter = "";
        _pathFilter = "";
        _sinceFilter = SinceOptions[0];
        OnPropertyChanged(nameof(AuthorFilter));
        OnPropertyChanged(nameof(PathFilter));
        OnPropertyChanged(nameof(SinceFilter));
        ApplyFilters();
    }

    private void ApplyFiltersSoon()
    {
        if (_filterDebounce is null)
        {
            _filterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _filterDebounce.Tick += (_, _) =>
            {
                _filterDebounce.Stop();
                ApplyFilters();
            };
        }
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }

    private void ApplyFilters()
    {
        _filterDebounce?.Stop();
        _maxCount = PageSize;
        OnPropertyChanged(nameof(IsFiltered));
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void ToggleFilters() => ShowFilters = !ShowFilters;

    // ---- Stash -------------------------------------------------------------------------------

    [RelayCommand]
    private async Task StashChanges()
    {
        var r = await Host.Dialogs.PromptAsync("Stash changes",
            "Save the uncommitted changes in a stash and clean the working tree. The stash shows in the graph above the commit it was made on; apply or pop it from there.",
            [new PromptField("Message", "", "Optional, to tell stashes apart"), new PromptField("Include untracked files", "true", IsCheckbox: true)],
            "Stash");
        if (r is null) return;
        await RunAsync(() => _repo.StashAsync(r[0], r[1] == "true"));
    }

    [RelayCommand]
    private async Task PopLatestStash()
    {
        if (_stashes.Count == 0)
        {
            Host.ReportError("There are no stashes to pop.");
            return;
        }
        await RunAsync(() => _repo.ApplyStashAsync(_stashes[0].Name, pop: true, restoreIndex: false));
    }

    /// <param name="pop">Drop the stash after it applied cleanly; on conflicts git keeps it.</param>
    public Task ApplyStashAsync(StashEntry stash, bool pop) => RunAsync(() => _repo.ApplyStashAsync(stash.Name, pop, restoreIndex: false));

    public async Task DropStashAsync(StashEntry stash)
    {
        if (!await Host.Dialogs.ConfirmAsync("Drop stash", $"Delete {stash.Name} (\"{stash.Message}\")? Its changes are lost.", "Drop stash", destructive: true))
            return;
        await RunAsync(() => _repo.DropStashAsync(stash.Name));
    }

    public async Task StashBranchAsync(StashEntry stash)
    {
        var r = await Host.Dialogs.PromptAsync("Create branch from stash",
            $"Create a branch at the commit {stash.Name} was made on, check it out, and apply the stash there. The stash is dropped if it applies cleanly.",
            [new PromptField("Branch name", "", "e.g. feature/resume-work")], "Create branch");
        if (r is null || string.IsNullOrWhiteSpace(r[0])) return;
        await RunAsync(() => _repo.StashBranchAsync(r[0].Trim(), stash.Name));
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

    public async Task RenameBranchAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { Kind: RefKind.LocalBranch } r) return;
        var res = await Host.Dialogs.PromptAsync("Rename branch", $"Rename the branch {r.Name}. Its remote copies keep their name.",
            [new PromptField("New name", r.Name)], "Rename");
        if (res is null || string.IsNullOrWhiteSpace(res[0]) || res[0].Trim() == r.Name) return;
        await RunAsync(() => _repo.RenameBranchAsync(r.Name, res[0].Trim()));
    }

    public async Task SetUpstreamAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { Kind: RefKind.LocalBranch } r) return;
        var remote = Sync?.Remotes.FirstOrDefault() ?? "origin";
        var res = await Host.Dialogs.PromptAsync("Set upstream",
            $"Track a remote branch with {r.Name}, so pull, push and the ↓↑ counts use it. Fetch first if the remote branch is new.",
            [new PromptField("Remote branch", r.Upstream ?? $"{remote}/{r.Name}")], "Set upstream");
        if (res is null || string.IsNullOrWhiteSpace(res[0])) return;
        await RunAsync(() => _repo.SetUpstreamAsync(r.Name, res[0].Trim()));
    }

    public async Task DeleteRemoteBranchAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { Kind: RefKind.RemoteBranch, Remote: { } remote } r) return;
        var branch = r.Name[(remote.Length + 1)..];
        if (!await Host.Dialogs.ConfirmAsync("Delete remote branch",
                $"Delete {branch} from {remote}? Everyone using {remote} loses it; local branches are not touched.", "Delete from remote", true))
            return;
        await RunSyncAsync(() => _repo.DeleteRemoteBranchAsync(remote, branch));
    }

    public async Task PushTagAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { Kind: RefKind.Tag } r) return;
        var remotes = Sync?.Remotes ?? [];
        if (remotes.Count == 0)
        {
            Host.ReportError("This repository has no remote to push the tag to.");
            return;
        }
        var res = await Host.Dialogs.PromptAsync("Push tag", $"Push the tag {r.Name} to a remote.",
            [new PromptField("Remote", remotes[0], Hint: remotes.Count > 1 ? $"One of {string.Join(", ", remotes)}" : null)], "Push tag");
        if (res is null || string.IsNullOrWhiteSpace(res[0])) return;
        await RunSyncAsync(() => _repo.PushTagAsync(res[0].Trim(), r.Name));
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
