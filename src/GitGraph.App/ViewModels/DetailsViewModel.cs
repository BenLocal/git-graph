using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    // The diff currently on screen, reused when the same file is opened full screen.
    private (DiffTarget Target, FileChange File, FileDiff Diff)? _shown;

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
        HasContent = false;
        Files.Clear();
        FileCount = 0;
        ResetDiff(null);
    }

    public void ShowCommit(CommitInfo c)
    {
        var key = "c:" + c.Hash;
        if (key == _key) return;
        _key = key;
        IsCompare = false;
        Heading = c.Subject;
        Body = null;
        HashText = c.Hash;
        AuthorText = $"{c.AuthorName} <{c.AuthorEmail}>";
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

    public void ShowUncommitted(string from)
    {
        // Always reload: the working tree changes under us, unlike a commit.
        _key = "w";
        IsCompare = false;
        Heading = "Uncommitted changes";
        Body = null;
        HashText = null;
        AuthorText = null;
        DateText = null;
        CommitterText = null;
        ParentsText = "Working tree and index compared with HEAD";
        RangeText = null;
        _ = LoadAsync(new DiffTarget(from, null), null);
    }

    public void ShowCompare(CommitInfo older, CommitInfo? newer)
    {
        var key = $"x:{older.Hash}:{newer?.Hash}";
        if (key == _key && newer != null) return;
        _key = key;
        IsCompare = true;
        Heading = newer is null ? $"{older.ShortHash} compared with your working tree" : $"{older.ShortHash} compared with {newer.ShortHash}";
        Body = null;
        HashText = null;
        AuthorText = null;
        DateText = null;
        CommitterText = null;
        ParentsText = null;
        RangeText = newer is null
            ? $"From \"{older.Subject}\" to uncommitted changes"
            : $"From \"{older.Subject}\" to \"{newer.Subject}\"";
        _ = LoadAsync(new DiffTarget(older.Hash, newer?.Hash), null);
    }

    private async Task LoadAsync(DiffTarget target, Func<CancellationToken, Task>? header)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;
        var keepFile = target.IsWorkingTree && _target?.IsWorkingTree == true ? SelectedFile?.Change.Path : null;
        _target = target;
        HasContent = true;
        try
        {
            var headerTask = header?.Invoke(ct) ?? Task.CompletedTask;
            var changes = await repo.GetChangesAsync(target, ct);
            await headerTask;
            if (ct.IsCancellationRequested) return;

            Files.Clear();
            foreach (var c in changes) Files.Add(new FileChangeViewModel(c));
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

    partial void OnSelectedFileChanged(FileChangeViewModel? value)
    {
        if (value is null || _target is null) return;
        _ = LoadDiffAsync(_target, value.Change);
    }

    private async Task LoadDiffAsync(DiffTarget target, FileChange file)
    {
        _diffCts?.Cancel();
        var cts = _diffCts = new CancellationTokenSource();
        try
        {
            var diff = await ComputeDiffAsync(target, file, cts.Token);
            if (cts.IsCancellationRequested) return;
            _shown = (target, file, diff);
            if (EmptyMessage(diff, file) is { } message)
            {
                ResetDiff(message);
                return;
            }
            DiffLines = diff.Lines.Select(l => new DiffLineViewModel(l)).ToList();
            SplitRows = diff.SplitRows.Select(r => new SplitRowViewModel(r)).ToList();
            DiffMessage = null;
            HasDiff = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is GitException or IOException)
        {
            ResetDiff($"Could not load this diff: {e.Message}");
        }
    }

    private async Task<FileDiff> ComputeDiffAsync(DiffTarget target, FileChange file, CancellationToken ct)
    {
        // git's numstat already knows binary files; skip reading them.
        if (file.IsBinary) return new FileDiff { Lines = [], SplitRows = [], IsBinary = true };
        var (oldText, newText) = await repo.GetFileVersionsAsync(target, file, ct);
        return await Task.Run(() => TextDiff.Build(oldText, newText), ct);
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
        if (file is null || _target is null) return;
        var target = _target;
        try
        {
            var diff = _shown is { } s && s.Target == target && s.File == file.Change
                ? s.Diff
                : await ComputeDiffAsync(target, file.Change, CancellationToken.None);
            host.Dialogs.ShowDiff(new FullDiffViewModel(file.Change, RangeLabel(target), diff, EmptyMessage(diff, file.Change)));
        }
        catch (Exception e) when (e is GitException or IOException)
        {
            host.ReportError($"Could not load the diff of {file.Change.Path}: {e.Message}");
        }
    }

    private static string RangeLabel(DiffTarget t)
    {
        static string Short(string rev) => rev == GitRepository.EmptyTree ? "empty tree" : rev.Length == 40 ? rev[..7] : rev;
        return $"{Short(t.From)} → {(t.To is null ? "working tree" : Short(t.To))}";
    }

    private void ResetDiff(string? message)
    {
        DiffLines = [];
        SplitRows = [];
        HasDiff = false;
        DiffMessage = message;
    }
}

public sealed class FileChangeViewModel(FileChange change)
{
    public FileChange Change { get; } = change;
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

public sealed class DiffLineViewModel(DiffLine line)
{
    public string OldNo { get; } = line.OldLine?.ToString() ?? "";
    public string NewNo { get; } = line.NewLine?.ToString() ?? "";
    public string Sign { get; } = line.Kind switch { DiffLineKind.Added => "+", DiffLineKind.Deleted => "−", _ => "" };
    public string Text { get; } = line.Text;
    public IReadOnlyList<DiffSegment>? Segments { get; } = line.Segments;
    public bool IsAdded { get; } = line.Kind == DiffLineKind.Added;
    public bool IsDeleted { get; } = line.Kind == DiffLineKind.Deleted;
    public bool IsHunk { get; } = line.Kind == DiffLineKind.Hunk;
}

public sealed class SplitCellViewModel(DiffLine? line, bool left)
{
    public string No { get; } = (left ? line?.OldLine : line?.NewLine)?.ToString() ?? "";
    public string Text { get; } = line?.Text ?? "";
    public IReadOnlyList<DiffSegment>? Segments { get; } = line?.Segments;
    public bool IsAdded { get; } = line?.Kind == DiffLineKind.Added;
    public bool IsDeleted { get; } = line?.Kind == DiffLineKind.Deleted;
    public bool IsEmpty { get; } = line is null;
}

public sealed class SplitRowViewModel
{
    public SplitRowViewModel(SplitDiffRow row)
    {
        IsHunk = row.IsHunk;
        HunkText = row.IsHunk ? row.Left!.Text : "";
        Left = new SplitCellViewModel(row.IsHunk ? null : row.Left, true);
        Right = new SplitCellViewModel(row.IsHunk ? null : row.Right, false);
    }

    public bool IsHunk { get; }
    public string HunkText { get; }
    public SplitCellViewModel Left { get; }
    public SplitCellViewModel Right { get; }
}

/// <summary>A file's diff shown side by side in its own window.</summary>
public sealed class FullDiffViewModel(FileChange file, string range, FileDiff diff, string? message)
{
    public string Path { get; } = file.Path;
    public string? OldPath { get; } = file.OldPath;
    public bool HasOldPath => OldPath != null;
    public string Range { get; } = range;
    public string? AddText { get; } = diff.Additions > 0 ? $"+{diff.Additions}" : null;
    public string? DelText { get; } = diff.Deletions > 0 ? $"−{diff.Deletions}" : null;
    public string? Message { get; } = message;
    public bool HasMessage => Message != null;
    public IReadOnlyList<SplitRowViewModel> Rows { get; } = diff.SplitRows.Select(r => new SplitRowViewModel(r)).ToList();
}
