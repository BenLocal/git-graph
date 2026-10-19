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

    [ObservableProperty]
    private bool _hasContent;

    [ObservableProperty]
    private string _heading = "";

    [ObservableProperty]
    private string? _body;

    [ObservableProperty]
    private string? _hashText;

    [ObservableProperty]
    private string? _authorText;

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
        get => host.SplitDiff;
        set
        {
            if (host.SplitDiff == value) return;
            host.SplitDiff = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUnified));
        }
    }

    public bool IsUnified => !IsSplit;

    public bool WrapLines => host.WrapDiffLines;

    /// <summary>Whitespace or wrapping changed in the View menu: redo the diff on screen.</summary>
    public void DiffOptionsChanged()
    {
        _shown = null;
        OnPropertyChanged(nameof(WrapLines));
        if (SelectedFile is { } file) _ = LoadDiffAsync(file);
    }

    /// <summary>The selected file exists in the working tree, so it can open in an editor.</summary>
    public bool CanOpenSelected => SelectedFile is { } f && File.Exists(FullPath(f.Change.Path));

    /// <summary>The panel is shared by every tab, like the panel in VS Code, so its visibility lives on the host.</summary>
    public bool IsPanelVisible
    {
        get => host.ShowDetailsPanel;
        set
        {
            if (host.ShowDetailsPanel == value) return;
            host.ShowDetailsPanel = value;
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
        HasContent = false;
        IsStaging = false;
        Files.Clear();
        Groups.Clear();
        FileCount = 0;
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
        AuthorText = $"{c.AuthorName} <{c.AuthorEmail}>";
        AuthorEmail = c.AuthorEmail;
        AuthorName = c.AuthorName;
        DateText = c.AuthorDate.ToString("yyyy-MM-dd HH:mm:ss zzz");
        CommitterText = null;
        ParentsText = null;
        RangeText = null;
        _ = LoadAsync(GitRepository.TargetForCommit(c), async ct =>
        {
            var d = await repo.GetCommitDetailAsync(c.Hash, ct);
            // A newer selection may have replaced this one while git was running.
            if (d is null || ct.IsCancellationRequested) return;
            var nl = d.Message.IndexOf('\n');
            Heading = nl < 0 ? d.Message : d.Message[..nl];
            Body = nl < 0 ? null : d.Message[(nl + 1)..].Trim('\n', '\r');
            if (string.IsNullOrWhiteSpace(Body)) Body = null;
            HashText = d.Hash;
            AuthorText = $"{d.AuthorName} <{d.AuthorEmail}>";
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
        });
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
        AuthorText = $"{stash.AuthorName} <{stash.AuthorEmail}>";
        AuthorEmail = stash.AuthorEmail;
        AuthorName = stash.AuthorName;
        DateText = stash.Date.ToString("yyyy-MM-dd HH:mm:ss zzz");
        CommitterText = null;
        ParentsText = $"{stash.Name}, made on {stash.BaseHash[..7]}. Changes shown against that commit"
            + (stash.HasUntracked ? ", with the untracked files it saved." : ".");
        RangeText = null;
        _ = LoadAsync(new DiffTarget(stash.BaseHash, stash.Hash), null, stash);
    }

    public void ShowUncommitted(string from)
    {
        // Arriving from another row: the changes are what there is to see (and stage); a refresh keeps the tab.
        if (_key != "w") SelectedTabIndex = 1;
        // Always reload: the working tree changes under us, unlike a commit.
        _key = "w";
        IsCompare = false;
        Heading = "Uncommitted changes";
        Body = null;
        HashText = null;
        AuthorText = null;
        AuthorEmail = null;
        DateText = null;
        CommitterText = null;
        ParentsText = "Working tree and index compared with HEAD";
        RangeText = null;
        _headRev = from;
        IsStaging = true;
        _ = LoadWorkingAsync();
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
        AuthorText = null;
        AuthorEmail = null;
        DateText = null;
        CommitterText = null;
        ParentsText = null;
        RangeText = newer is null
            ? $"From \"{older.Subject}\" to uncommitted changes"
            : $"From \"{older.Subject}\" to \"{newer.Subject}\"";
        _ = LoadAsync(new DiffTarget(older.Hash, newer?.Hash), null);
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
            var changes = await repo.GetChangesAsync(target, ct);
            var untracked = stash is null ? [] : await repo.GetStashUntrackedAsync(stash, ct);
            await headerTask;
            if (ct.IsCancellationRequested) return;

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
            if (!ct.IsCancellationRequested) host.ReportError(e.Message);
        }
    }

    private async Task LoadWorkingAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;
        var keep = _workingFile;
        _target = new DiffTarget(_headRev, null);
        HasContent = true;
        try
        {
            var changes = await repo.GetWorkingChangesAsync(ct);
            if (ct.IsCancellationRequested) return;
            // The same changes as on screen (the usual case for a refresh): keep the list, and only check the diff.
            var signature = string.Join('\n', new[] { changes.Conflicted, changes.Staged, changes.Unstaged }
                .Select((list, i) => string.Join('\n', list.Select(c => $"{i}|{c}"))));
            if (signature == _workingSignature && Files.Count > 0)
            {
                if (SelectedFile is { } current) _ = LoadDiffAsync(current);
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
            if (!ct.IsCancellationRequested) host.ReportError(e.Message);
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
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
        if (newValue is { Group: not FileGroup.Commit }) _workingFile = (newValue.Group, newValue.Change.Path);
    }

    partial void OnSelectedFileChanged(FileChangeViewModel? value)
    {
        OnPropertyChanged(nameof(CanOpenSelected));
        if (value is null) return;
        _ = LoadDiffAsync(value);
    }

    private async Task LoadDiffAsync(FileChangeViewModel fileVm)
    {
        _diffCts?.Cancel();
        var cts = _diffCts = new CancellationTokenSource();
        var (target, file) = (fileVm.Target, fileVm.Change);
        try
        {
            if (IsImage(file.Path) && await LoadImagesAsync(fileVm, cts.Token)) return;
            var loaded = await ComputeDiffAsync(target, file, cts.Token);
            if (cts.IsCancellationRequested) return;
            var (diff, syntax) = (loaded.Diff, loaded.Syntax);
            // Reloaded by a refresh with nothing changed: leave the diff (and its scroll position) alone.
            var same = _shown is { } s && s.Target == target && s.File == file && s.Loaded.OldText == loaded.OldText
                && s.Loaded.NewText == loaded.NewText && HasDiff;
            _shown = (target, file, loaded);
            if (same) return;
            if (EmptyMessage(diff, file) is { } message)
            {
                ResetDiff(message);
                return;
            }
            SetImages(null, null);
            var hunk = -1;
            DiffLines = diff.Lines.Select(l => new DiffLineViewModel(l, syntax, l.Kind == DiffLineKind.Hunk ? HunkActionsFor(fileVm, ++hunk) : null)).ToList();
            hunk = -1;
            SplitRows = diff.SplitRows.Select(r => new SplitRowViewModel(r, syntax, r.IsHunk ? HunkActionsFor(fileVm, ++hunk) : null)).ToList();
            DiffMessage = null;
            HasDiff = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is GitException or IOException)
        {
            ResetDiff($"Could not load this diff: {e.Message}");
        }
    }

    private async Task<LoadedDiff> ComputeDiffAsync(DiffTarget target, FileChange file, CancellationToken ct)
    {
        // git's numstat already knows binary files; skip reading them.
        if (file.IsBinary) return new LoadedDiff(new FileDiff { Lines = [], SplitRows = [], IsBinary = true }, DiffSyntax.Empty, "", "");
        var (oldText, newText) = await repo.GetFileVersionsAsync(target, file, ct);
        return await Task.Run(() =>
        {
            var diff = TextDiff.Build(oldText, newText, ignoreWhitespace: host.IgnoreWhitespace);
            return new LoadedDiff(diff, SyntaxHighlighter.Highlight(file.Path, diff), oldText, newText);
        }, ct);
    }

    private static string? EmptyMessage(FileDiff diff, FileChange file) =>
        diff.IsBinary ? "Binary file. There is no text diff to show."
        : diff.Lines.Count > 0 ? null
        : file.Kind == ChangeKind.Renamed ? "Renamed without content changes."
        : "No text changes, only file mode, line endings or metadata.";

    /// <summary>Opens one file's diff side by side in its own maximized window.</summary>
    [RelayCommand]
    private async Task OpenFullDiff(FileChangeViewModel? file)
    {
        file ??= SelectedFile;
        if (file is null) return;
        var target = file.Target;
        try
        {
            var (diff, syntax, _, _) = _shown is { } s && s.Target == target && s.File == file.Change
                ? s.Loaded
                : await ComputeDiffAsync(target, file.Change, CancellationToken.None);
            host.Dialogs.ShowDiff(new FullDiffViewModel(file.Change, RangeLabel(target), diff, syntax, EmptyMessage(diff, file.Change),
                host.Dialogs, FullPath(file.Change.Path)));
        }
        catch (Exception e) when (e is GitException or IOException)
        {
            host.ReportError($"Could not load the diff of {file.Change.Path}: {e.Message}");
        }
    }

    // ---- Staging and committing ------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private string _commitMessage = "";

    /// <summary>Replace the last commit instead of adding one; turning it on brings back that commit's message.</summary>
    [ObservableProperty]
    private bool _amend;

    [ObservableProperty]
    private int _stagedCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private bool _isCommitting;

    partial void OnAmendChanged(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(CommitMessage)) _ = LoadLastMessageAsync();
    }

    private async Task LoadLastMessageAsync()
    {
        try
        {
            var message = await repo.GetLastCommitMessageAsync();
            if (Amend && string.IsNullOrWhiteSpace(CommitMessage)) CommitMessage = message;
        }
        catch (GitException) { /* no commits yet: nothing to bring back */ }
    }

    private bool CanCommit() => !IsCommitting && !string.IsNullOrWhiteSpace(CommitMessage);

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task Commit()
    {
        var unstaged = FilesIn(FileGroup.Unstaged);
        if (StagedCount == 0 && !Amend)
        {
            // Like VS Code's smart commit, but asked each time.
            if (unstaged.Count == 0)
            {
                host.ReportError("There is nothing to commit. Resolve the merge conflicts and stage the files first.");
                return;
            }
            if (!await host.Dialogs.ConfirmAsync("Commit all changes",
                    $"Nothing is staged. Stage all {Count(unstaged.Count)} and commit them?", "Stage all and commit"))
                return;
        }
        IsCommitting = true;
        try
        {
            if (StagedCount == 0 && !Amend) await repo.StageAsync(unstaged.Select(f => f.Change.Path));
            await repo.CommitAsync(CommitMessage, Amend);
            CommitMessage = "";
            Amend = false;
        }
        catch (GitException e) { host.ReportError(e.Message); }
        finally { IsCommitting = false; }
        await NotifyRepoChangedAsync();
    }

    [RelayCommand]
    private Task Stage(FileChangeViewModel file) => ChangeRepoAsync(() => repo.StageAsync([file.Change.Path]));

    [RelayCommand]
    private Task Unstage(FileChangeViewModel file) =>
        ChangeRepoAsync(() => repo.UnstageAsync(file.Change.OldPath is { } old ? [old, file.Change.Path] : [file.Change.Path]));

    [RelayCommand]
    private async Task Discard(FileChangeViewModel file)
    {
        var question = file.Change.Kind == ChangeKind.Untracked
            ? $"Delete {file.FileName}? Git isn't tracking it, so it can't be brought back."
            : $"Discard your changes to {file.FileName}? This can't be undone.";
        if (!await host.Dialogs.ConfirmAsync("Discard changes", question, file.Change.Kind == ChangeKind.Untracked ? "Delete file" : "Discard changes", destructive: true))
            return;
        await ChangeRepoAsync(() => repo.DiscardAsync([file.Change]));
    }

    [RelayCommand]
    private Task StageAll(FileGroupViewModel group) => ChangeRepoAsync(() => repo.StageAsync(group.Files.Select(f => f.Change.Path)));

    [RelayCommand]
    private Task UnstageAll(FileGroupViewModel group) =>
        ChangeRepoAsync(() => repo.UnstageAsync(group.Files.SelectMany(f => f.Change.OldPath is { } old ? [old, f.Change.Path] : new[] { f.Change.Path })));

    [RelayCommand]
    private async Task DiscardAll(FileGroupViewModel group)
    {
        var untracked = group.Files.Count(f => f.Change.Kind == ChangeKind.Untracked);
        var question = $"Discard all {Count(group.Files.Count)}? This can't be undone."
            + (untracked > 0 ? $" {Count(untracked, "untracked file")} will be deleted." : "");
        if (!await host.Dialogs.ConfirmAsync("Discard all changes", question, "Discard all", destructive: true)) return;
        await ChangeRepoAsync(() => repo.DiscardAsync(group.Files.Select(f => f.Change).ToList()));
    }

    [RelayCommand]
    private Task StageHunk(HunkActions hunk) => ApplyHunkAsync(hunk, cached: true, reverse: false);

    [RelayCommand]
    private Task UnstageHunk(HunkActions hunk) => ApplyHunkAsync(hunk, cached: true, reverse: true);

    [RelayCommand]
    private async Task DiscardHunk(HunkActions hunk)
    {
        if (!await host.Dialogs.ConfirmAsync("Discard change", "Discard this change from the working tree? This can't be undone.",
                "Discard change", destructive: true))
            return;
        await ApplyHunkAsync(hunk, cached: false, reverse: true);
    }

    /// <summary>Resolves a conflict with the version from the checked-out branch.</summary>
    [RelayCommand]
    private Task TakeOurs(FileChangeViewModel file) => ChangeRepoAsync(() => repo.TakeSideAsync([file.Change.Path], ours: true));

    /// <summary>Resolves a conflict with the version being merged, picked, reverted or replayed.</summary>
    [RelayCommand]
    private Task TakeTheirs(FileChangeViewModel file) => ChangeRepoAsync(() => repo.TakeSideAsync([file.Change.Path], ours: false));

    [RelayCommand]
    private void OpenInVsCode(FileChangeViewModel? file)
    {
        if ((file ?? SelectedFile) is { } f && ExternalTools.OpenInVsCode(FullPath(f.Change.Path)) is { } error) host.ReportError(error);
    }

    /// <summary>Applies one hunk of the diff on screen; the staged side's hunks come out of the index in reverse.</summary>
    private async Task ApplyHunkAsync(HunkActions hunk, bool cached, bool reverse)
    {
        if (_shown is not { } s || s.Target != hunk.File.Target || s.File != hunk.File.Change) return;
        var patch = TextDiff.HunkPatch(s.File.Path, s.Loaded.OldText, s.Loaded.NewText, s.Loaded.Diff, hunk.Index);
        await ChangeRepoAsync(() => repo.ApplyPatchAsync(patch, cached, reverse));
    }

    /// <summary>Hunks of a staged or unstaged edit can be moved on their own; added, deleted and renamed files go whole.</summary>
    private HunkActions? HunkActionsFor(FileChangeViewModel file, int index) =>
        file.Group is FileGroup.Staged or FileGroup.Unstaged && file.Change.Kind == ChangeKind.Modified
            ? new HunkActions(this, file, index)
            : null;

    private List<FileChangeViewModel> FilesIn(FileGroup group) => Files.Where(f => f.Group == group).ToList();

    private static string Count(int n, string noun = "change") => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private async Task ChangeRepoAsync(Func<Task> action)
    {
        try { await action(); }
        catch (GitException e) { host.ReportError(e.Message); }
        await NotifyRepoChangedAsync();
    }

    private Task NotifyRepoChangedAsync() => RepoChanged?.Invoke() ?? Task.CompletedTask;

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
        host.Dialogs.ShowFileHistory(new FileHistoryViewModel(repo, file.Change.Path, rev, host.Dialogs, blame));
    }

    // ---- Copying -----------------------------------------------------------------------------

    /// <summary>Confirms a copy in a small note that fades out on its own.</summary>
    [ObservableProperty]
    private string? _copiedNote;

    [ObservableProperty]
    private bool _isCopiedNoteVisible;

    private int _copyCount;

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

    private string FullPath(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(repo.Path, relative));

    private async Task CopyAsync(string? text, string note)
    {
        if (string.IsNullOrEmpty(text)) return;
        await host.Dialogs.CopyTextAsync(text);
        CopiedNote = note;
        IsCopiedNoteVisible = true;
        var count = ++_copyCount;
        await Task.Delay(1600);
        if (count == _copyCount) IsCopiedNoteVisible = false;
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

    // ---- Images ------------------------------------------------------------------------------

    /// <summary>An image file's before and after, shown side by side instead of a text diff.</summary>
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _oldImage;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _newImage;

    public bool IsImageDiff => OldImage != null || NewImage != null;
    public string OldImageText => ImageText(OldImage, "Added in this change");
    public string NewImageText => ImageText(NewImage, "Deleted in this change");

    private static string ImageText(Avalonia.Media.Imaging.Bitmap? image, string missing) =>
        image is null ? missing : $"{image.PixelSize.Width} × {image.PixelSize.Height}";

    private static bool IsImage(string path) =>
        FolderTabViewModel.ImageExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    private void SetImages(Avalonia.Media.Imaging.Bitmap? oldImage, Avalonia.Media.Imaging.Bitmap? newImage)
    {
        var (o, n) = (OldImage, NewImage);
        OldImage = oldImage;
        NewImage = newImage;
        o?.Dispose();
        n?.Dispose();
        OnPropertyChanged(nameof(IsImageDiff));
        OnPropertyChanged(nameof(OldImageText));
        OnPropertyChanged(nameof(NewImageText));
    }

    private async Task<bool> LoadImagesAsync(FileChangeViewModel file, CancellationToken ct)
    {
        var (oldBytes, newBytes) = await repo.GetFileBytesAsync(file.Target, file.Change, ct);
        if (ct.IsCancellationRequested) return true;
        static Avalonia.Media.Imaging.Bitmap? Decode(byte[]? bytes)
        {
            if (bytes is null) return null;
            try { return new Avalonia.Media.Imaging.Bitmap(new MemoryStream(bytes)); }
            catch (Exception) { return null; }
        }
        var (oldImage, newImage) = (Decode(oldBytes), Decode(newBytes));
        // Neither side decodes (an SVG, a corrupt file): fall back to the usual binary message.
        if (oldImage is null && newImage is null) return false;
        ResetDiff(null);
        SetImages(oldImage, newImage);
        HasDiff = true;
        return true;
    }
}

/// <summary>The diff on screen with the texts it was built from, which hunk patches need.</summary>
public sealed record LoadedDiff(FileDiff Diff, DiffSyntax Syntax, string OldText, string NewText);

/// <summary>Where a file in the changes list comes from: a commit (or comparison), or one of the staging groups.</summary>
public enum FileGroup { Commit, Conflicted, Staged, Unstaged }

/// <summary>The stage / unstage / discard buttons on a hunk header of an uncommitted file's diff.</summary>
public sealed record HunkActions(DetailsViewModel Owner, FileChangeViewModel File, int Index)
{
    public bool CanStage => File.Group == FileGroup.Unstaged;
    public bool CanUnstage => File.Group == FileGroup.Staged;
    public bool CanDiscard => File.Group == FileGroup.Unstaged;
}

/// <summary>A heading in the uncommitted changes list, with buttons for all of its files.</summary>
public sealed class FileGroupViewModel(FileGroup group, string title, IReadOnlyList<FileChangeViewModel> files, DetailsViewModel owner)
{
    public FileGroup Group { get; } = group;
    public string Title { get; } = title;
    public IReadOnlyList<FileChangeViewModel> Files { get; } = files;
    public DetailsViewModel Owner { get; } = owner;
    public bool CanStage => Group is FileGroup.Unstaged or FileGroup.Conflicted;
    public bool CanUnstage => Group == FileGroup.Staged;
    public bool CanDiscard => Group == FileGroup.Unstaged;
    public string StageTip => Group == FileGroup.Conflicted ? "Mark all as resolved (stage)" : "Stage all changes";
}

public sealed partial class FileChangeViewModel(FileChange change, DetailsViewModel owner, FileGroup group, DiffTarget target) : ObservableObject
{
    public FileChange Change { get; } = change;
    public FileGroup Group { get; } = group;
    /// <summary>What this file's diff compares: the commit's parent and the commit, HEAD and the index, and so on.</summary>
    public DiffTarget Target { get; } = target;

    /// <summary>For the grouped list of uncommitted files, which draws its own selection.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public bool CanStage => Group is FileGroup.Unstaged or FileGroup.Conflicted;
    public bool CanUnstage => Group == FileGroup.Staged;
    public bool CanDiscard => Group == FileGroup.Unstaged;
    public string StageTip => Group == FileGroup.Conflicted ? "Mark as resolved (stage)" : "Stage";
    public string DiscardTip => Change.Kind == ChangeKind.Untracked ? "Delete file" : "Discard changes";
    public bool IsConflicted => Group == FileGroup.Conflicted;
    /// <summary>A file that exists in the working tree, to open in an editor.</summary>
    public bool CanOpen => Group is FileGroup.Unstaged or FileGroup.Staged or FileGroup.Conflicted && Change.Kind != ChangeKind.Deleted;
    /// <summary>For the file's context menu, which sits outside the list's visual tree.</summary>
    public DetailsViewModel Owner { get; } = owner;
    public string FileName { get; } = System.IO.Path.GetFileName(change.Path);
    public string Directory { get; } = System.IO.Path.GetDirectoryName(change.Path)?.Replace('\\', '/') ?? "";
    public bool HasDirectory => Directory.Length > 0;
    public string Status => Change.StatusLetter;
    public string Tooltip => Change.OldPath is null ? Change.Path : $"{Change.OldPath} → {Change.Path}";
    public string? AddText => Change.Additions is > 0 ? $"+{Change.Additions}" : null;
    public string? DelText => Change.Deletions is > 0 ? $"−{Change.Deletions}" : null;
    public bool IsAdded => Change.Kind is ChangeKind.Added or ChangeKind.Untracked;
    public bool IsDeleted => Change.Kind == ChangeKind.Deleted;
    public bool IsModified => !IsAdded && !IsDeleted;
}

public sealed class DiffLineViewModel(DiffLine line, DiffSyntax syntax, HunkActions? hunk = null)
{
    public string OldNo { get; } = line.OldLine?.ToString() ?? "";
    public string NewNo { get; } = line.NewLine?.ToString() ?? "";
    public string Sign { get; } = line.Kind switch { DiffLineKind.Added => "+", DiffLineKind.Deleted => "−", _ => "" };
    public string Text { get; } = line.Text;
    public IReadOnlyList<DiffSegment>? Segments { get; } = line.Segments;
    public SyntaxSpan[]? Syntax { get; } = syntax.For(line);
    public bool IsAdded { get; } = line.Kind == DiffLineKind.Added;
    public bool IsDeleted { get; } = line.Kind == DiffLineKind.Deleted;
    public bool IsHunk { get; } = line.Kind == DiffLineKind.Hunk;
    /// <summary>Set on the hunk headers of an uncommitted file that can be staged a hunk at a time.</summary>
    public HunkActions? Hunk { get; } = hunk;
}

public sealed class SplitCellViewModel(DiffLine? line, bool left, DiffSyntax syntax)
{
    public string No { get; } = (left ? line?.OldLine : line?.NewLine)?.ToString() ?? "";
    public string Text { get; } = line?.Text ?? "";
    public IReadOnlyList<DiffSegment>? Segments { get; } = line?.Segments;
    public SyntaxSpan[]? Syntax { get; } = syntax.For(line);
    public bool IsAdded { get; } = line?.Kind == DiffLineKind.Added;
    public bool IsDeleted { get; } = line?.Kind == DiffLineKind.Deleted;
    public bool IsEmpty { get; } = line is null;
}

public sealed class SplitRowViewModel
{
    public SplitRowViewModel(SplitDiffRow row, DiffSyntax syntax, HunkActions? hunk = null)
    {
        Hunk = hunk;
        IsHunk = row.IsHunk;
        HunkText = row.IsHunk ? row.Left!.Text : "";
        Left = new SplitCellViewModel(row.IsHunk ? null : row.Left, true, syntax);
        Right = new SplitCellViewModel(row.IsHunk ? null : row.Right, false, syntax);
    }

    public bool IsHunk { get; }
    public HunkActions? Hunk { get; }
    public string HunkText { get; }
    public SplitCellViewModel Left { get; }
    public SplitCellViewModel Right { get; }
}

/// <summary>
/// A file's diff shown side by side in its own window: the whole file, with long unchanged
/// stretches folded into rows the user can open and close.
/// </summary>
public sealed partial class FullDiffViewModel : ObservableObject
{
    private readonly Services.IDialogService _dialogs;
    private readonly string _fullPath;

    public FullDiffViewModel(FileChange file, string range, FileDiff diff, DiffSyntax syntax, string? message, Services.IDialogService dialogs, string fullPath)
    {
        _dialogs = dialogs;
        _fullPath = fullPath;
        Path = file.Path;
        OldPath = file.OldPath;
        Range = range;
        AddText = diff.Additions > 0 ? $"+{diff.Additions}" : null;
        DelText = diff.Deletions > 0 ? $"−{diff.Deletions}" : null;
        Message = message;
        foreach (var (start, count, foldable) in TextDiff.Sections(diff.AllRows))
        {
            var rows = diff.AllRows.Skip(start).Take(count).Select(r => new SplitRowViewModel(r, syntax)).ToList();
            if (foldable)
            {
                var fold = new FoldViewModel(rows);
                _folds.Add(fold);
                Items.Add(fold);
            }
            else
                foreach (var r in rows) Items.Add(r);
        }
    }

    private readonly List<FoldViewModel> _folds = [];

    public string Path { get; }
    public string? OldPath { get; }
    public bool HasOldPath => OldPath != null;
    public string Range { get; }
    public string? AddText { get; }
    public string? DelText { get; }
    public string? Message { get; }
    public bool HasMessage => Message != null;
    public bool HasFolds => _folds.Count > 0;

    /// <summary>Diff rows and fold rows in display order; opening a fold inserts its rows after it.</summary>
    public ObservableCollection<object> Items { get; } = [];

    [ObservableProperty]
    private bool _isCopied;

    [RelayCommand]
    private Task CopyPath() => CopyAsync(Path);

    [RelayCommand]
    private Task CopyFullPath() => CopyAsync(_fullPath);

    private async Task CopyAsync(string text)
    {
        await _dialogs.CopyTextAsync(text);
        // The copy button shows a check mark for a moment.
        IsCopied = true;
        await Task.Delay(1400);
        IsCopied = false;
    }

    [RelayCommand]
    private void ToggleFold(FoldViewModel fold) => SetExpanded(fold, !fold.IsExpanded);

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var f in _folds) SetExpanded(f, true);
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var f in _folds) SetExpanded(f, false);
    }

    private void SetExpanded(FoldViewModel fold, bool expanded)
    {
        if (fold.IsExpanded == expanded) return;
        var index = Items.IndexOf(fold);
        if (expanded)
            for (var i = 0; i < fold.Rows.Count; i++) Items.Insert(index + 1 + i, fold.Rows[i]);
        else
            for (var i = 0; i < fold.Rows.Count; i++) Items.RemoveAt(index + 1);
        fold.IsExpanded = expanded;
    }
}

/// <summary>A stretch of unchanged lines that can be shown or hidden.</summary>
public sealed partial class FoldViewModel(IReadOnlyList<SplitRowViewModel> rows) : ObservableObject
{
    public IReadOnlyList<SplitRowViewModel> Rows { get; } = rows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isExpanded;

    public string Label => IsExpanded
        ? $"Hide {Rows.Count} unchanged lines"
        : $"{Rows.Count} unchanged lines";
}
