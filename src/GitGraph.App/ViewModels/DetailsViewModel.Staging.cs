using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>Uncommitted changes: selecting several files, staging, unstaging, discarding and committing.</summary>
public partial class DetailsViewModel
{
    // ---- Selecting several uncommitted files -------------------------------------------------

    // Where a Shift selection starts: the last file picked without Shift.
    private FileChangeViewModel? _anchor;

    /// <summary>A click in the uncommitted list: Ctrl adds or removes a file, Shift selects from the last one picked.</summary>
    public void SelectWorkingFile(FileChangeViewModel file, bool toggle, bool range)
    {
        if (range && _anchor != null && Files.Contains(_anchor))
        {
            var (a, b) = (Files.IndexOf(_anchor), Files.IndexOf(file));
            for (var i = 0; i < Files.Count; i++) Files[i].IsSelected = i >= Math.Min(a, b) && i <= Math.Max(a, b);
            SelectedFile = file;
            return;
        }
        if (toggle)
        {
            file.IsSelected = !file.IsSelected;
            _anchor = file;
            // The diff shows the file just added, or stays on another selected one.
            SelectedFile = file.IsSelected ? file : Files.FirstOrDefault(f => f.IsSelected) ?? SelectedFile;
            return;
        }
        foreach (var f in Files) f.IsSelected = f == file;
        _anchor = file;
        SelectedFile = file;
    }

    /// <summary>Arrow keys in the uncommitted list; with Shift the selection grows from the anchor.</summary>
    public void MoveWorkingSelection(int delta, bool extend)
    {
        if (Files.Count == 0) return;
        var next = Files[Math.Clamp((SelectedFile is { } f ? Files.IndexOf(f) : -1) + delta, 0, Files.Count - 1)];
        SelectWorkingFile(next, toggle: false, range: extend);
    }

    /// <summary>The files a row's button acts on: the selected files of its group if it is one of them, else just it.</summary>
    private List<FileChangeViewModel> Targets(FileChangeViewModel file) =>
        file.IsSelected ? Files.Where(f => f.IsSelected && f.Group == file.Group).ToList() : [file];

    /// <summary>Space in the uncommitted list: stage the selected files, or unstage them if they are staged.</summary>
    public Task ToggleStageSelectedAsync() => SelectedFile switch
    {
        { CanStage: true } f => Stage(f),
        { CanUnstage: true } f => Unstage(f),
        _ => Task.CompletedTask,
    };

    /// <summary>Delete in the uncommitted list.</summary>
    public Task DiscardSelectedAsync() => SelectedFile is { CanDiscard: true } f ? Discard(f) : Task.CompletedTask;

    partial void OnSelectedFileChanged(FileChangeViewModel? value)
    {
        OnPropertyChanged(nameof(CanOpenSelected));
        if (value is null) return;
        _ = Loads.Track(LoadDiffAsync(value));
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
            // Reloaded by a refresh with nothing changed: leave the diff (and its scroll position and colours) alone.
            if (_shown is { } s && s.Target == target && s.File == file && s.Loaded.OldText == loaded.OldText
                && s.Loaded.NewText == loaded.NewText && HasDiff)
                return;
            _shown = (target, file, loaded);
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
            ShowComments();
            DiffMessage = null;
            HasDiff = true;
            _ = Loads.Track(ColourAsync(target, file, loaded, cts.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is GitException or IOException)
        {
            ResetDiff($"Could not load this diff: {e.Message}");
        }
    }

    /// <summary>Adds syntax colours to the diff on screen once they are worked out; picking another file stops it.</summary>
    private async Task ColourAsync(DiffTarget target, FileChange file, LoadedDiff loaded, CancellationToken ct)
    {
        try
        {
            var syntax = await Task.Run(() => SyntaxHighlighter.Highlight(file.Path, loaded.Diff, ct), ct);
            if (ct.IsCancellationRequested || _shown is not { } s || !ReferenceEquals(s.Loaded, loaded)) return;
            _shown = (target, file, loaded with { Syntax = syntax });
            foreach (var line in DiffLines) line.ApplySyntax(syntax);
            foreach (var row in SplitRows) row.ApplySyntax(syntax);
        }
        catch (OperationCanceledException) { }
    }

    private async Task<LoadedDiff> ComputeDiffAsync(DiffTarget target, FileChange file, CancellationToken ct)
    {
        // git's numstat already knows binary files; skip reading them.
        if (file.IsBinary) return new LoadedDiff(new FileDiff { Lines = [], SplitRows = [], IsBinary = true }, DiffSyntax.Empty, "", "");
        var (oldText, newText) = await _repo.GetFileVersionsAsync(target, file, ct);
        // Without colours: those take longer than everything else, so they follow (see ColourAsync).
        return await Task.Run(() =>
            new LoadedDiff(TextDiff.Build(oldText, newText, ignoreWhitespace: _host.IgnoreWhitespace), DiffSyntax.Empty, oldText, newText), ct);
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
            // Opened before the colours arrived (or for another file): the window gets them before it shows.
            if (syntax == DiffSyntax.Empty) syntax = await Task.Run(() => SyntaxHighlighter.Highlight(file.Change.Path, diff));
            _host.Dialogs.ShowDiff(new FullDiffViewModel(file.Change, RangeLabel(target), diff, syntax, EmptyMessage(diff, file.Change),
                _host.Dialogs, FullPath(file.Change.Path), CommentsFor(file)));
        }
        catch (Exception e) when (e is GitException or IOException)
        {
            _host.ReportError($"Could not load the diff of {file.Change.Path}: {e.Message}");
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
    [NotifyPropertyChangedFor(nameof(StagedText))]
    private int _stagedCount;

    public string StagedText => StagedCount == 0
        ? "Nothing is staged yet. Stage changes on the Changes tab, or Commit offers to stage them all first."
        : $"{Count(StagedCount, "staged change")} will be committed. Stage or unstage them on the Changes tab.";

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
            var message = await _repo.GetLastCommitMessageAsync();
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
                _host.ReportError("There is nothing to commit. Resolve the merge conflicts and stage the files first.");
                return;
            }
            if (!await _host.Dialogs.ConfirmAsync("Commit all changes",
                    $"Nothing is staged. Stage all {Count(unstaged.Count)} and commit them?", "Stage all and commit"))
                return;
        }
        IsCommitting = true;
        try
        {
            if (StagedCount == 0 && !Amend) await _repo.StageAsync(unstaged.Select(f => f.Change.Path));
            await _repo.CommitAsync(CommitMessage, Amend);
            CommitMessage = "";
            Amend = false;
        }
        catch (GitException e) { _host.ReportError(e.Message); }
        finally { IsCommitting = false; }
        await NotifyRepoChangedAsync();
    }

    [RelayCommand]
    private Task Stage(FileChangeViewModel file) => ChangeRepoAsync(() => _repo.StageAsync(Targets(file).Select(f => f.Change.Path)));

    [RelayCommand]
    private Task Unstage(FileChangeViewModel file) =>
        ChangeRepoAsync(() => _repo.UnstageAsync(Targets(file).SelectMany(f => f.Change.OldPath is { } old ? [old, f.Change.Path] : new[] { f.Change.Path })));

    [RelayCommand]
    private async Task Discard(FileChangeViewModel file)
    {
        var targets = Targets(file);
        var untracked = targets.Count(f => f.Change.Kind == ChangeKind.Untracked);
        var question = targets.Count > 1
            ? $"Discard your changes to {targets.Count} files? This can't be undone."
              + (untracked > 0 ? $" {Count(untracked, "untracked file")} will be deleted." : "")
            : file.Change.Kind == ChangeKind.Untracked
                ? $"Delete {file.FileName}? Git isn't tracking it, so it can't be brought back."
                : $"Discard your changes to {file.FileName}? This can't be undone.";
        var confirm = targets.Count == 1 && file.Change.Kind == ChangeKind.Untracked ? "Delete file" : "Discard changes";
        if (!await _host.Dialogs.ConfirmAsync("Discard changes", question, confirm, destructive: true)) return;
        await ChangeRepoAsync(() => _repo.DiscardAsync(targets.Select(f => f.Change).ToList()));
    }

    [RelayCommand]
    private Task StageAll(FileGroupViewModel group) => ChangeRepoAsync(() => _repo.StageAsync(group.Files.Select(f => f.Change.Path)));

    [RelayCommand]
    private Task UnstageAll(FileGroupViewModel group) =>
        ChangeRepoAsync(() => _repo.UnstageAsync(group.Files.SelectMany(f => f.Change.OldPath is { } old ? [old, f.Change.Path] : new[] { f.Change.Path })));

    [RelayCommand]
    private async Task DiscardAll(FileGroupViewModel group)
    {
        var untracked = group.Files.Count(f => f.Change.Kind == ChangeKind.Untracked);
        var question = $"Discard all {Count(group.Files.Count)}? This can't be undone."
            + (untracked > 0 ? $" {Count(untracked, "untracked file")} will be deleted." : "");
        if (!await _host.Dialogs.ConfirmAsync("Discard all changes", question, "Discard all", destructive: true)) return;
        await ChangeRepoAsync(() => _repo.DiscardAsync(group.Files.Select(f => f.Change).ToList()));
    }

    [RelayCommand]
    private Task StageHunk(HunkActions hunk) => ApplyHunkAsync(hunk, cached: true, reverse: false);

    [RelayCommand]
    private Task UnstageHunk(HunkActions hunk) => ApplyHunkAsync(hunk, cached: true, reverse: true);

    [RelayCommand]
    private async Task DiscardHunk(HunkActions hunk)
    {
        if (!await _host.Dialogs.ConfirmAsync("Discard change", "Discard this change from the working tree? This can't be undone.",
                "Discard change", destructive: true))
            return;
        await ApplyHunkAsync(hunk, cached: false, reverse: true);
    }

    /// <summary>Resolves a conflict with the version from the checked-out branch.</summary>
    [RelayCommand]
    private Task TakeOurs(FileChangeViewModel file) => ChangeRepoAsync(() => _repo.TakeSideAsync([file.Change.Path], ours: true));

    /// <summary>Resolves a conflict with the version being merged, picked, reverted or replayed.</summary>
    [RelayCommand]
    private Task TakeTheirs(FileChangeViewModel file) => ChangeRepoAsync(() => _repo.TakeSideAsync([file.Change.Path], ours: false));

    [RelayCommand]
    private void OpenInVsCode(FileChangeViewModel? file)
    {
        if ((file ?? SelectedFile) is { } f && ExternalTools.OpenInVsCode(FullPath(f.Change.Path)) is { } error) _host.ReportError(error);
    }

    /// <summary>Applies one hunk of the diff on screen; the staged side's hunks come out of the index in reverse.</summary>
    private async Task ApplyHunkAsync(HunkActions hunk, bool cached, bool reverse)
    {
        if (_shown is not { } s || s.Target != hunk.File.Target || s.File != hunk.File.Change) return;
        var patch = TextDiff.HunkPatch(s.File.Path, s.Loaded.OldText, s.Loaded.NewText, s.Loaded.Diff, hunk.Index);
        await ChangeRepoAsync(() => _repo.ApplyPatchAsync(patch, cached, reverse));
    }

    /// <summary>
    /// Hunks of a staged or unstaged edit can be moved on their own; added, deleted and renamed files go whole. Not
    /// while whitespace is ignored: the hunks then hide changes, and a patch built from them wouldn't match the file.
    /// </summary>
    private HunkActions? HunkActionsFor(FileChangeViewModel file, int index) =>
        file.Group is FileGroup.Staged or FileGroup.Unstaged && file.Change.Kind == ChangeKind.Modified && !_host.IgnoreWhitespace
            ? new HunkActions(this, file, index)
            : null;

    private List<FileChangeViewModel> FilesIn(FileGroup group) => Files.Where(f => f.Group == group).ToList();

    private static string Count(int n, string noun = "change") => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private async Task ChangeRepoAsync(Func<Task> action)
    {
        try { await action(); }
        catch (GitException e) { _host.ReportError(e.Message); }
        await NotifyRepoChangedAsync();
    }

    private Task NotifyRepoChangedAsync() => RepoChanged?.Invoke() ?? Task.CompletedTask;
}
