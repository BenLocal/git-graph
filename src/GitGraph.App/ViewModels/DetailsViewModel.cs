using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>The lower pane: what a commit (or the working tree, or a pair of commits) changed, and the diff of one file.</summary>
public partial class DetailsViewModel(GitRepository repo, IAppHost host) : ObservableObject
{
    // Fields rather than the constructor parameters, so the other parts of this class (Staging, Images) can use them.
    private readonly GitRepository _repo = repo;
    private readonly IAppHost _host = host;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _diffCts;
    private DiffTarget? _target;
    private string? _key;
    // The diff currently on screen, reused when the same file is opened full screen and for hunk patches.
    private (DiffTarget Target, FileChange File, LoadedDiff Loaded)? _shown;
    // What uncommitted changes compare with: HEAD, or the empty tree before the first commit.
    private string _headRev = "HEAD";
    // The uncommitted file last looked at. A refresh briefly clears the selection, so it is kept here.
    private (FileGroup Group, string Path)? _workingFile;
    // The uncommitted file list on screen, to leave it alone when a refresh finds the same changes.
    private string? _workingSignature;
    // The same for a commit's, stash's or comparison's file list.
    private string? _listSignature;
    // The image pair on screen, so a refresh that finds the same bytes doesn't decode and swap them again.
    private string? _imageSignature;

    [ObservableProperty]
    private bool _hasContent;

    [ObservableProperty]
    private string _heading = "";

    [ObservableProperty]
    private string? _body;

    [ObservableProperty]
    private string? _hashText;

    /// <summary>Drives the author's avatar.</summary>
    [ObservableProperty]
    private string? _authorEmail;

    [ObservableProperty]
    private string? _authorName;

    [ObservableProperty]
    private string? _dateText;

    [ObservableProperty]
    private string? _parentsText;

    [ObservableProperty]
    private string? _committerText;

    [ObservableProperty]
    private string? _rangeText;

    [ObservableProperty]
    private bool _isCompare;

    public ObservableCollection<FileChangeViewModel> Files { get; } = [];

    /// <summary>Uncommitted files grouped as git's staging sees them: conflicts, staged, and not staged.</summary>
    public ObservableCollection<FileGroupViewModel> Groups { get; } = [];

    /// <summary>Showing uncommitted changes, which can be staged and committed (not a commit or a comparison).</summary>
    [ObservableProperty]
    private bool _isStaging;

    /// <summary>Called after anything here changes the repository, so the graph and this pane catch up.</summary>
    public Func<Task>? RepoChanged { get; set; }

    [ObservableProperty]
    private string? _filesSummary;

    [ObservableProperty]
    private int _fileCount;

    /// <summary>0 shows the commit message, 1 the changed files; kept while the selection moves.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private FileChangeViewModel? _selectedFile;

    [ObservableProperty]
    private IReadOnlyList<DiffLineViewModel> _diffLines = [];

    [ObservableProperty]
    private IReadOnlyList<SplitRowViewModel> _splitRows = [];

    [ObservableProperty]
    private string? _diffMessage;

    [ObservableProperty]
    private bool _hasDiff;

    public bool IsSplit
    {
        get => _host.SplitDiff;
        set
        {
            if (_host.SplitDiff == value) return;
            _host.SplitDiff = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUnified));
        }
    }

    public bool IsUnified => !IsSplit;

    public bool WrapLines => _host.WrapDiffLines;

    /// <summary>Whitespace or wrapping changed in the View menu: redo the diff on screen.</summary>
    public void DiffOptionsChanged()
    {
        _shown = null;
        OnPropertyChanged(nameof(WrapLines));
        if (SelectedFile is { } file) _ = Loads.Track(LoadDiffAsync(file));
    }

    /// <summary>The selected file exists in the working tree, so it can open in an editor.</summary>
    public bool CanOpenSelected => SelectedFile is { } f && File.Exists(FullPath(f.Change.Path));

    /// <summary>The panel is shared by every tab, like the panel in VS Code, so its visibility lives on the _host.</summary>
    public bool IsPanelVisible
    {
        get => _host.ShowDetailsPanel;
        set
        {
            if (_host.ShowDetailsPanel == value) return;
            _host.ShowDetailsPanel = value;
            if (!value) IsMaximized = false;
            OnPropertyChanged();
        }
    }

    /// <summary>The panel takes the graph's space; the toolbar stays.</summary>
    [ObservableProperty]
    private bool _isMaximized;

    [RelayCommand]
    private void ClosePanel() => IsPanelVisible = false;

    [RelayCommand]
    private void ToggleMaximize() => IsMaximized = !IsMaximized;

    /// <summary>Re-reads the shared visibility after another tab changed it.</summary>
    public void SyncPanel()
    {
        if (!IsPanelVisible) IsMaximized = false;
        OnPropertyChanged(nameof(IsPanelVisible));
    }

    [RelayCommand]
    private void ShowSplit() => IsSplit = true;

    [RelayCommand]
    private void ShowUnified() => IsSplit = false;

    public void Clear()
    {
        _cts?.Cancel();
        _diffCts?.Cancel();
        _key = null;
        _target = null;
        _workingSignature = null;
        _listSignature = null;
        HasContent = false;
        IsStaging = false;
        Files.Clear();
        Groups.Clear();
        FileCount = 0;
        LoadComments(null);
        ResetDiff(null);
    }

    public void ShowCommit(CommitInfo c)
    {
        var key = "c:" + c.Hash;
        if (key == _key) return;
        _key = key;
        IsCompare = false;
        IsStaging = false;
        Heading = c.Subject;
        Body = null;
        HashText = c.Hash;
        AuthorEmail = c.AuthorEmail;
        AuthorName = c.AuthorName;
        DateText = c.AuthorDate.ToString("yyyy-MM-dd HH:mm:ss zzz");
        CommitterText = null;
        ParentsText = null;
        RangeText = null;
        LoadComments(c.Hash);
        _ = Loads.Track(LoadAsync(GitRepository.TargetForCommit(c), async ct =>
        {
            var d = await _repo.GetCommitDetailAsync(c.Hash, ct);
            // A newer selection may have replaced this one while git was running.
            if (d is null || ct.IsCancellationRequested) return;
            var nl = d.Message.IndexOf('\n');
            Heading = nl < 0 ? d.Message : d.Message[..nl];
            Body = nl < 0 ? null : d.Message[(nl + 1)..].Trim('\n', '\r');
            if (string.IsNullOrWhiteSpace(Body)) Body = null;
            HashText = d.Hash;
            AuthorEmail = d.AuthorEmail;
            AuthorName = d.AuthorName;
            DateText = d.AuthorDate.ToString("yyyy-MM-dd HH:mm:ss zzz");
            CommitterText = d.CommitterName != d.AuthorName || d.CommitterEmail != d.AuthorEmail
                ? $"Committed by {d.CommitterName} on {d.CommitDate:yyyy-MM-dd HH:mm}"
                : null;
            ParentsText = d.Parents.Count switch
            {
                0 => "Root commit",
                1 => $"Parent {d.Parents[0][..7]}",
                _ => $"Merge of {string.Join(" and ", d.Parents.Select(p => p[..7]))}. Changes shown against the first parent.",
            };
        }));
    }

    public void ShowStash(StashEntry stash)
    {
        var key = "s:" + stash.Hash;
        if (key == _key) return;
        _key = key;
        IsCompare = false;
        IsStaging = false;
        Heading = stash.Message;
        Body = null;
        HashText = stash.Hash;
        AuthorEmail = stash.AuthorEmail;
        AuthorName = stash.AuthorName;
        DateText = stash.Date.ToString("yyyy-MM-dd HH:mm:ss zzz");
        CommitterText = null;
        ParentsText = $"{stash.Name}, made on {stash.BaseHash[..7]}. Changes shown against that commit"
            + (stash.HasUntracked ? ", with the untracked files it saved." : ".");
        RangeText = null;
        LoadComments(null);
        _ = Loads.Track(LoadAsync(new DiffTarget(stash.BaseHash, stash.Hash), null, stash));
    }

    /// <param name="untracked">Untracked files a refresh just found, so they aren't looked for again.</param>
    public void ShowUncommitted(string from, IReadOnlyList<string>? untracked = null)
    {
        // Arriving from another row: the changes are what there is to see (and stage); a refresh keeps the tab.
        if (_key != "w") SelectedTabIndex = 1;
        // Always reload: the working tree changes under us, unlike a commit.
        _key = "w";
        IsCompare = false;
        Heading = "Uncommitted changes";
        Body = null;
        HashText = null;
        AuthorEmail = null;
        DateText = null;
        CommitterText = null;
        ParentsText = "Working tree and index compared with HEAD";
        RangeText = null;
        _headRev = from;
        IsStaging = true;
        LoadComments(null);
        _ = Loads.Track(LoadWorkingAsync(untracked));
    }

    public void ShowCompare(CommitInfo older, CommitInfo? newer)
    {
        var key = $"x:{older.Hash}:{newer?.Hash}";
        if (key == _key && newer != null) return;
        _key = key;
        IsCompare = true;
        IsStaging = false;
        Heading = newer is null ? $"{older.ShortHash} compared with your working tree" : $"{older.ShortHash} compared with {newer.ShortHash}";
        Body = null;
        HashText = null;
        AuthorEmail = null;
        DateText = null;
        CommitterText = null;
        ParentsText = null;
        RangeText = newer is null
            ? $"From \"{older.Subject}\" to uncommitted changes"
            : $"From \"{older.Subject}\" to \"{newer.Subject}\"";
        LoadComments(null);
        _ = Loads.Track(LoadAsync(new DiffTarget(older.Hash, newer?.Hash), null));
    }

    /// <param name="stash">Adds the untracked files a stash saved, which aren't in the stash commit itself.</param>
    private async Task LoadAsync(DiffTarget target, Func<CancellationToken, Task>? header, StashEntry? stash = null)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;
        var keepFile = target.IsWorkingTree && _target?.IsWorkingTree == true ? SelectedFile?.Change.Path : null;
        _target = target;
        _workingSignature = null;
        HasContent = true;
        try
        {
            var headerTask = header?.Invoke(ct) ?? Task.CompletedTask;
            var changes = await _repo.GetChangesAsync(target, ct);
            var untracked = stash is null ? [] : await _repo.GetStashUntrackedAsync(stash, ct);
            await headerTask;
            if (ct.IsCancellationRequested) return;

            // Reloaded with the same files (a refresh of a comparison with the working tree): keep the list, check the diff.
            var signature = $"{target}\n{string.Join('\n', changes)}\n{string.Join('\n', untracked)}";
            if (signature == _listSignature && Files.Count > 0)
            {
                if (SelectedFile is { } current) _ = Loads.Track(LoadDiffAsync(current));
                return;
            }
            _listSignature = signature;
            Files.Clear();
            Groups.Clear();
            foreach (var c in changes) Files.Add(new FileChangeViewModel(c, this, FileGroup.Commit, target));
            // A stash keeps its untracked files in its third parent.
            foreach (var path in untracked)
            {
                var c = new FileChange(ChangeKind.Untracked, path, null, null, null);
                changes.Add(c);
                Files.Add(new FileChangeViewModel(c, this, FileGroup.Commit, new DiffTarget(GitRepository.EmptyTree, stash!.Hash + "^3")));
            }
            FileCount = changes.Count;
            var adds = changes.Sum(c => c.Additions ?? 0);
            var dels = changes.Sum(c => c.Deletions ?? 0);
            FilesSummary = changes.Count == 0 ? "No file changes"
                : $"{changes.Count} {(changes.Count == 1 ? "file" : "files")}, +{adds} −{dels}";

            SelectedFile = (keepFile is null ? null : Files.FirstOrDefault(f => f.Change.Path == keepFile)) ?? Files.FirstOrDefault();
            if (SelectedFile is null) ResetDiff(changes.Count == 0 ? "Nothing changed here." : null);
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            if (!ct.IsCancellationRequested) _host.ReportError(e.Message);
        }
    }

    private async Task LoadWorkingAsync(IReadOnlyList<string>? untracked)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;
        var keep = _workingFile;
        _target = new DiffTarget(_headRev, null);
        _listSignature = null;
        HasContent = true;
        try
        {
            var changes = await _repo.GetWorkingChangesAsync(untracked, ct);
            if (ct.IsCancellationRequested) return;
            // The same changes as on screen (the usual case for a refresh): keep the list, and only check the diff.
            var signature = string.Join('\n', new[] { changes.Conflicted, changes.Staged, changes.Unstaged }
                .Select((list, i) => string.Join('\n', list.Select(c => $"{i}|{c}"))));
            if (signature == _workingSignature && Files.Count > 0)
            {
                if (SelectedFile is { } current) _ = Loads.Track(LoadDiffAsync(current));
                return;
            }
            _workingSignature = signature;
            Files.Clear();
            Groups.Clear();
            // A conflicted file shows with its conflict markers, against HEAD.
            AddGroup(FileGroup.Conflicted, "Merge conflicts", changes.Conflicted, new DiffTarget(_headRev, null));
            AddGroup(FileGroup.Staged, "Staged changes", changes.Staged, new DiffTarget(_headRev, GitRepository.Index));
            AddGroup(FileGroup.Unstaged, "Changes", changes.Unstaged, new DiffTarget(GitRepository.Index, null));
            StagedCount = changes.Staged.Count;
            FileCount = changes.Count;
            FilesSummary = null;
            // Stay on the same file; after it was staged or unstaged, follow it to its new group.
            SelectedFile = Files.FirstOrDefault(f => f.Group == keep?.Group && f.Change.Path == keep?.Path)
                ?? Files.FirstOrDefault(f => f.Change.Path == keep?.Path)
                ?? Files.FirstOrDefault();
            if (SelectedFile is null) ResetDiff("Nothing to commit: the working tree matches HEAD.");
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            if (!ct.IsCancellationRequested) _host.ReportError(e.Message);
        }
    }

    private void AddGroup(FileGroup group, string title, IReadOnlyList<FileChange> changes, DiffTarget target)
    {
        if (changes.Count == 0) return;
        var files = changes.Select(c => new FileChangeViewModel(c, this, group, target)).ToList();
        foreach (var f in files) Files.Add(f);
        Groups.Add(new FileGroupViewModel(group, title, files, this));
    }

    partial void OnSelectedFileChanged(FileChangeViewModel? oldValue, FileChangeViewModel? newValue)
    {
        // Picked on its own (not added to a selection by SelectWorkingFile): it becomes the only selected file.
        if (newValue is { IsSelected: false })
        {
            foreach (var f in Files) f.IsSelected = false;
            newValue.IsSelected = true;
            _anchor = newValue;
        }
        if (newValue is { Group: not FileGroup.Commit }) _workingFile = (newValue.Group, newValue.Change.Path);
    }

    // ---- History and blame -----------------------------------------------------------------

    [RelayCommand]
    private void FileHistory(FileChangeViewModel? file) => ShowHistory(file ?? SelectedFile, blame: false);

    [RelayCommand]
    private void Blame(FileChangeViewModel? file) => ShowHistory(file ?? SelectedFile, blame: true);

    /// <summary>A commit's file is followed from that commit; an uncommitted one from the working tree.</summary>
    private void ShowHistory(FileChangeViewModel? file, bool blame)
    {
        if (file is null) return;
        var rev = file.Target.To is { } to && to != GitRepository.Index ? to : null;
        // A file deleted by the commit only exists before it.
        if (file.Change.Kind == ChangeKind.Deleted && rev != null) rev = file.Target.From;
        _host.Dialogs.ShowFileHistory(new FileHistoryViewModel(_repo, file.Change.Path, rev, _host.Dialogs, blame));
    }

    // ---- Copying -----------------------------------------------------------------------------

    /// <summary>Confirms a copy in a small note that fades out on its own.</summary>
    public CopyNote CopyNote { get; } = new();

    /// <summary>The user's custom commands, run in this repository; set by the repository tab.</summary>
    public RepoCommands? Commands { get; init; }

    /// <summary>The file list, diff and colours being loaded for what is selected.</summary>
    public PendingLoads Loads { get; } = new();

    [RelayCommand]
    private Task CopyPath(FileChangeViewModel? file) =>
        CopyAsync((file ?? SelectedFile)?.Change.Path, "Path copied");

    [RelayCommand]
    private Task CopyFullPath(FileChangeViewModel? file) =>
        CopyAsync((file ?? SelectedFile)?.Change.Path is { } p ? FullPath(p) : null, "Full path copied");

    [RelayCommand]
    private Task CopyHash() => CopyAsync(HashText, "Commit hash copied");

    /// <summary>The whole message as written: subject, blank line, body (Markdown source, not the rendering).</summary>
    [RelayCommand]
    private Task CopyMessage() => CopyAsync(Body is null ? Heading : $"{Heading}\n\n{Body}", "Message copied");

    public Task CopyTextAsync(string text) => CopyAsync(text, "Copied");

    internal string FullPathOf(string relative) => FullPath(relative);

    private string FullPath(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(_repo.Path, relative));

    private async Task CopyAsync(string? text, string note)
    {
        if (string.IsNullOrEmpty(text)) return;
        await _host.Dialogs.CopyTextAsync(text);
        CopyNote.Show(note);
    }

    private static string RangeLabel(DiffTarget t)
    {
        static string Short(string rev) => rev == GitRepository.EmptyTree ? "empty tree" : rev == GitRepository.Index ? "staged" : rev.Length == 40 ? rev[..7] : rev;
        return $"{Short(t.From)} → {(t.To is null ? "working tree" : Short(t.To))}";
    }

    private void ResetDiff(string? message)
    {
        DiffLines = [];
        SplitRows = [];
        HasDiff = false;
        DiffMessage = message;
        SetImages(null, null);
    }

}
