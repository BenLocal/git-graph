using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>
/// One file's history (the commits that changed it, through renames, each with its diff) and its blame (who last
/// changed each line), in their own window.
/// </summary>
public sealed partial class FileHistoryViewModel : ObservableObject
{
    private const int MaxCommits = 500;
    private const int TabSize = 4;
    private readonly GitRepository _repo;
    private readonly IDialogService _dialogs;
    private readonly string? _rev;
    private bool _blameLoaded;
    private CancellationTokenSource? _diffCts;

    /// <param name="path">Relative to the repository's top level.</param>
    /// <param name="rev">The commit to start from and to blame; null for the working tree.</param>
    public FileHistoryViewModel(GitRepository repo, string path, string? rev, IDialogService dialogs, bool blame)
    {
        _repo = repo;
        _dialogs = dialogs;
        _rev = rev;
        Path = path;
        RevText = rev is null ? "Working tree" : $"At {rev[..Math.Min(7, rev.Length)]}";
        _selectedTabIndex = blame ? 1 : 0;
        _ = LoadHistoryAsync();
        if (blame) _ = LoadBlameAsync();
    }

    public string Path { get; }
    public string RevText { get; }

    /// <summary>0 shows the history, 1 the blame.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 1) _ = LoadBlameAsync();
    }

    // ---- History ---------------------------------------------------------------------------

    public ObservableCollection<FileHistoryRowViewModel> Entries { get; } = [];

    [ObservableProperty]
    private string? _historyMessage = "Loading…";

    [ObservableProperty]
    private FileHistoryRowViewModel? _selectedEntry;

    [ObservableProperty]
    private IReadOnlyList<DiffLineViewModel> _diffLines = [];

    [ObservableProperty]
    private string? _diffMessage;

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _repo.GetFileHistoryAsync(Path, _rev, MaxCommits);
            foreach (var e in history) Entries.Add(new FileHistoryRowViewModel(e, this));
            HistoryMessage = history.Count == 0 ? "No commit has changed this file yet."
                : history.Count == MaxCommits ? $"The latest {MaxCommits} commits that changed this file." : null;
            SelectedEntry ??= Entries.FirstOrDefault();
        }
        catch (GitException e) { HistoryMessage = e.Message; }
    }

    partial void OnSelectedEntryChanged(FileHistoryRowViewModel? value)
    {
        if (value != null) _ = LoadDiffAsync(value);
    }

    private async Task LoadDiffAsync(FileHistoryRowViewModel row)
    {
        _diffCts?.Cancel();
        var cts = _diffCts = new CancellationTokenSource();
        try
        {
            var c = row.Entry.Commit;
            var target = new DiffTarget(c.Parents.Count > 0 ? c.Parents[0] : GitRepository.EmptyTree, c.Hash);
            // The log has no line counts, and a change without them reads as binary; the diff finds binary files itself.
            var change = row.Entry.Change with { Additions = 0, Deletions = 0 };
            var (oldText, newText) = await _repo.GetFileVersionsAsync(target, change, cts.Token);
            var (diff, syntax) = await Task.Run(() =>
            {
                var d = TextDiff.Build(oldText, newText);
                return (d, SyntaxHighlighter.Highlight(change.Path, d));
            }, cts.Token);
            if (cts.IsCancellationRequested) return;
            DiffLines = diff.Lines.Select(l => new DiffLineViewModel(l, syntax)).ToList();
            DiffMessage = diff.IsBinary ? "Binary file. There is no text diff to show."
                : diff.Lines.Count == 0 ? "The file's content didn't change in this commit." : null;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is GitException or IOException)
        {
            DiffLines = [];
            DiffMessage = $"Could not load this diff: {e.Message}";
        }
    }

    // ---- Blame -----------------------------------------------------------------------------

    [ObservableProperty]
    private IReadOnlyList<BlameRowViewModel> _blameLines = [];

    [ObservableProperty]
    private string? _blameMessage = "Loading…";

    private async Task LoadBlameAsync()
    {
        if (_blameLoaded) return;
        _blameLoaded = true;
        try
        {
            var blame = await _repo.BlameAsync(Path, _rev);
            var tab = new string(' ', TabSize);
            var rows = await Task.Run(() =>
            {
                var texts = blame.Lines.Select(l => (l.Number, Text: l.Text.Replace("\t", tab))).ToList();
                var syntax = SyntaxHighlighter.HighlightLines(Path, texts);
                var list = new List<BlameRowViewModel>(texts.Count);
                var odd = true;
                for (var i = 0; i < blame.Lines.Count; i++)
                {
                    var line = blame.Lines[i];
                    var start = i == 0 || blame.Lines[i - 1].Hash != line.Hash;
                    if (start) odd = !odd;
                    list.Add(new BlameRowViewModel(line.Number, texts[i].Text, syntax.GetValueOrDefault(line.Number),
                        blame.Commits[line.Hash], start, odd, this));
                }
                return list;
            });
            BlameLines = rows;
            BlameMessage = rows.Count == 0 ? "The file is empty." : null;
        }
        catch (GitException e) { BlameMessage = e.Message; }
    }

    /// <summary>From a blame line to its commit in the history.</summary>
    [RelayCommand]
    private void ShowCommit(BlameRowViewModel line)
    {
        if (!line.Commit.IsCommitted) return;
        if (Entries.FirstOrDefault(e => e.Entry.Commit.Hash == line.Commit.Hash) is not { } entry)
        {
            HistoryMessage = $"{line.Commit.Hash[..7]} is older than the commits listed here.";
            SelectedTabIndex = 0;
            return;
        }
        SelectedTabIndex = 0;
        SelectedEntry = entry;
    }

    [RelayCommand]
    private Task CopyHash(string hash) => _dialogs.CopyTextAsync(hash);

    [RelayCommand]
    private Task CopyPath() => _dialogs.CopyTextAsync(Path);
}

public sealed class FileHistoryRowViewModel(FileHistoryEntry entry, FileHistoryViewModel owner)
{
    public FileHistoryEntry Entry { get; } = entry;
    public FileHistoryViewModel Owner { get; } = owner;
    public string Hash => Entry.Commit.Hash;
    public string ShortHash => Entry.Commit.ShortHash;
    public string Subject => Entry.Commit.Subject;
    public string Author => Entry.Commit.AuthorName;
    public string AuthorEmail => Entry.Commit.AuthorEmail;
    public string DateText => Entry.Commit.AuthorDate.ToString("yyyy-MM-dd HH:mm");
    public string Status => Entry.Change.StatusLetter;
    public bool IsAdded => Entry.Change.Kind == ChangeKind.Added;
    public bool IsDeleted => Entry.Change.Kind == ChangeKind.Deleted;
    /// <summary>"Renamed from …" on the commit where the file got its current name.</summary>
    public string? RenameText => Entry.Change.OldPath is { } old ? $"Renamed from {old}" : null;
    public bool HasRename => RenameText != null;
}

/// <summary>A line of the blamed file; the first line of each run from the same commit names it.</summary>
public sealed class BlameRowViewModel(int number, string text, SyntaxSpan[]? syntax, BlameCommit commit, bool isBlockStart, bool isOddBlock,
    FileHistoryViewModel owner)
{
    public int Number { get; } = number;
    public string NumberText => Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string Text { get; } = text;
    public SyntaxSpan[]? Syntax { get; } = syntax;
    public BlameCommit Commit { get; } = commit;
    public bool IsBlockStart { get; } = isBlockStart;
    /// <summary>Every other run of lines is shaded, so where one commit's lines end is visible.</summary>
    public bool IsOddBlock { get; } = isOddBlock;
    public FileHistoryViewModel Owner { get; } = owner;

    public string Summary => !IsBlockStart ? "" : Commit.IsCommitted ? Commit.Summary : "Not committed yet";
    public string Meta => !IsBlockStart || !Commit.IsCommitted ? "" : $"{Commit.AuthorName}, {Commit.Date:yyyy-MM-dd}";
    public string Tooltip => Commit.IsCommitted
        ? $"{Commit.Summary}\n{Commit.AuthorName} <{Commit.AuthorEmail}>, {Commit.Date:yyyy-MM-dd HH:mm}\n{Commit.Hash}\nClick to show this commit in the history."
        : "Changed in the working tree, not committed yet.";
}
