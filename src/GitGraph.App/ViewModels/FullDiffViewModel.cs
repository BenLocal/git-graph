using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>
/// A file's diff in its own window, as in Beyond Compare or Meld: each side shows its whole file, and the changes are
/// joined across the middle. Long unchanged stretches fold; the keyboard moves from one change to the next.
/// </summary>
public sealed partial class FullDiffViewModel : ObservableObject
{
    private readonly Services.IDialogService _dialogs;
    private readonly string _fullPath;
    private readonly IReadOnlyList<SplitDiffRow> _rows;
    // The row of each line of each side, to find a line's counterpart.
    private readonly int[] _oldRow, _newRow;

    /// <param name="comments">The commit's review comments, when the diff is one commit's (lines can take comments).</param>
    public FullDiffViewModel(FileChange file, string range, FileDiff diff, DiffSyntax syntax, string? message, Services.IDialogService dialogs, string fullPath,
        CommitComments? comments = null)
    {
        _dialogs = dialogs;
        _comments = comments;
        _fullPath = fullPath;
        Path = file.Path;
        OldPath = file.OldPath;
        Range = range;
        AddText = diff.Additions > 0 ? $"+{diff.Additions}" : null;
        DelText = diff.Deletions > 0 ? $"−{diff.Deletions}" : null;
        Message = message;
        Syntax = syntax;

        _rows = diff.AllRows;
        var oldLines = new List<DiffLine>();
        var newLines = new List<DiffLine>();
        var oldRow = new List<int>();
        var newRow = new List<int>();
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Left is { } left) { oldLines.Add(left); oldRow.Add(i); }
            if (_rows[i].Right is { } right) { newLines.Add(right); newRow.Add(i); }
        }
        OldLines = oldLines;
        NewLines = newLines;
        _oldRow = [.. oldRow];
        _newRow = [.. newRow];
        Blocks = TextDiff.Blocks(_rows);
        Changes = Blocks.Where(b => b.Changed).ToList();
        // Foldable stretches are unchanged, so both sides have the same lines there.
        Folds = TextDiff.Sections(_rows).Where(s => s.Foldable)
            .Select(s => new FoldViewModel(_rows[s.Start].Left!.OldLine!.Value - 1, _rows[s.Start].Right!.NewLine!.Value - 1, s.Count))
            .ToList();
        if (comments != null)
        {
            comments.PropertyChanged += OnCommentsChanged;
            ShowComments();
        }
    }

    public string Path { get; }
    public string? OldPath { get; }
    public bool HasOldPath => OldPath != null;
    public string Range { get; }
    public string? AddText { get; }
    public string? DelText { get; }
    public string? Message { get; }
    public bool HasMessage => Message != null;

    public DiffSyntax Syntax { get; }
    /// <summary>Each side's file, line by line (tabs expanded).</summary>
    public IReadOnlyList<DiffLine> OldLines { get; }
    public IReadOnlyList<DiffLine> NewLines { get; }
    public IReadOnlyList<DiffBlock> Blocks { get; }
    public IReadOnlyList<DiffBlock> Changes { get; }
    public IReadOnlyList<FoldViewModel> Folds { get; }
    public bool HasFolds => Folds.Count > 0;
    public bool HasChanges => Changes.Count > 0;

    /// <summary>The change being looked at, an index into <see cref="Changes"/>; -1 before the first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangePosition))]
    private int _currentChange = -1;

    public string ChangePosition => Changes.Count == 0 ? "No changes"
        : CurrentChange < 0 ? $"{Changes.Count} changes"
        : $"{CurrentChange + 1} of {Changes.Count}";

    /// <summary>Asks the view to scroll a change into view and put the caret on it.</summary>
    public event Action<DiffBlock>? RevealRequested;

    [RelayCommand]
    private void NextChange() => GoToChange(CurrentChange + 1);

    [RelayCommand]
    private void PreviousChange() => GoToChange(CurrentChange < 0 ? -1 : CurrentChange - 1);

    /// <summary>Moves to a change; past either end it wraps around, as in VS Code.</summary>
    public void GoToChange(int index)
    {
        if (Changes.Count == 0) return;
        CurrentChange = (index % Changes.Count + Changes.Count) % Changes.Count;
        RevealRequested?.Invoke(Changes[CurrentChange]);
    }

    // The caret's line and its counterpart on the other side, one above the other (Beyond Compare's line details).
    [ObservableProperty]
    private DiffLine? _detailOld;

    [ObservableProperty]
    private DiffLine? _detailNew;

    public string DetailOldNo => DetailOld?.OldLine?.ToString() ?? "";
    public string DetailNewNo => DetailNew?.NewLine?.ToString() ?? "";
    public SyntaxSpan[]? DetailOldSyntax => DetailOld?.OldLine is { } n ? Syntax.Line(true, n) : null;
    public SyntaxSpan[]? DetailNewSyntax => DetailNew?.NewLine is { } n ? Syntax.Line(false, n) : null;
    public bool IsDetailDeleted => DetailOld?.Kind == DiffLineKind.Deleted;
    public bool IsDetailAdded => DetailNew?.Kind == DiffLineKind.Added;

    /// <summary>The caret moved to <paramref name="line"/> (0-based) of one side.</summary>
    public void SetCaret(bool old, int line)
    {
        var rows = old ? _oldRow : _newRow;
        if (line < 0 || line >= rows.Length) return;
        var row = _rows[rows[line]];
        DetailOld = row.Left;
        DetailNew = row.Right;
        OnPropertyChanged(nameof(DetailOldNo));
        OnPropertyChanged(nameof(DetailNewNo));
        OnPropertyChanged(nameof(DetailOldSyntax));
        OnPropertyChanged(nameof(DetailNewSyntax));
        OnPropertyChanged(nameof(IsDetailDeleted));
        OnPropertyChanged(nameof(IsDetailAdded));
        for (var i = 0; i < Changes.Count; i++)
        {
            var (start, count) = old ? (Changes[i].OldStart, Changes[i].OldCount) : (Changes[i].NewStart, Changes[i].NewCount);
            if (line >= start && line < start + count) CurrentChange = i;
        }
    }

    // ---- Review comments ---------------------------------------------------------------------

    private readonly CommitComments? _comments;

    /// <summary>Lines can take comments: the diff is one commit's.</summary>
    public bool CanComment => _comments != null;

    /// <summary>The file's comments, in the order they were made.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommentsTitle))]
    private IReadOnlyList<CommentViewModel> _fileComments = [];

    public string CommentsTitle => FileComments.Count == 0 ? "Comments" : $"Comments ({FileComments.Count})";

    /// <summary>The comments list beside the code: hidden until asked for, so the diff gets the whole width.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommentsPanelVisible))]
    private bool _showCommentsPanel;

    public bool IsCommentsPanelVisible => CanComment && ShowCommentsPanel;

    /// <summary>The 0-based lines of each side that have comments, for the markers in the editors.</summary>
    public IReadOnlySet<int> OldCommentLines { get; private set; } = new HashSet<int>();
    public IReadOnlySet<int> NewCommentLines { get; private set; } = new HashSet<int>();

    /// <summary>Asks the view to show a 0-based line of one side and put the caret on it.</summary>
    public event Action<bool, int>? LineRevealRequested;

    /// <summary>Comments on 0-based <paramref name="line"/> of the old or new side.</summary>
    public Task CommentOnLineAsync(bool old, int line) => _comments?.AddAsync(Path, old, line + 1) ?? Task.CompletedTask;

    public void RevealComment(CommentViewModel comment) => LineRevealRequested?.Invoke(comment.Comment.OldSide, comment.Comment.Line - 1);

    /// <summary>The window closed: stop following the commit's comments, which outlive it.</summary>
    public void Detach()
    {
        if (_comments != null) _comments.PropertyChanged -= OnCommentsChanged;
    }

    private void OnCommentsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => ShowComments();

    private void ShowComments()
    {
        var comments = _comments!.For(Path).ToList();
        OldCommentLines = comments.Where(c => c.OldSide).Select(c => c.Line - 1).ToHashSet();
        NewCommentLines = comments.Where(c => !c.OldSide).Select(c => c.Line - 1).ToHashSet();
        FileComments = comments.Select(c => new CommentViewModel(c, _comments)).ToList();
    }

    /// <summary>The copy button's check mark.</summary>
    public CopyNote Copied { get; } = new();

    [RelayCommand]
    private Task CopyPath() => CopyAsync(Path);

    [RelayCommand]
    private Task CopyFullPath() => CopyAsync(_fullPath);

    private async Task CopyAsync(string text)
    {
        await _dialogs.CopyTextAsync(text);
        Copied.Show();
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var f in Folds) f.IsExpanded = true;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var f in Folds) f.IsExpanded = false;
    }
}

/// <summary>A stretch of unchanged lines that can be shown or hidden, starting at these 0-based lines of each side.</summary>
public sealed partial class FoldViewModel(int oldStart, int newStart, int count) : ObservableObject
{
    public int OldStart { get; } = oldStart;
    public int NewStart { get; } = newStart;
    public int Count { get; } = count;
    public string Label => $"{Count} unchanged lines";

    [ObservableProperty]
    private bool _isExpanded;
}
