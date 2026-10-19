using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

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

public sealed partial class DiffLineViewModel(DiffLine line, DiffSyntax syntax, HunkActions? hunk = null) : ObservableObject
{
    public string OldNo { get; } = line.OldLine?.ToString() ?? "";
    public string NewNo { get; } = line.NewLine?.ToString() ?? "";
    public string Sign { get; } = line.Kind switch { DiffLineKind.Added => "+", DiffLineKind.Deleted => "−", _ => "" };
    public string Text { get; } = line.Text;
    public IReadOnlyList<DiffSegment>? Segments { get; } = line.Segments;
    /// <summary>Colours arrive after the diff is on screen; see <see cref="ApplySyntax"/>.</summary>
    [ObservableProperty]
    private SyntaxSpan[]? _syntax = syntax.For(line);

    public void ApplySyntax(DiffSyntax s) => Syntax = s.For(line);

    public bool IsAdded { get; } = line.Kind == DiffLineKind.Added;
    public bool IsDeleted { get; } = line.Kind == DiffLineKind.Deleted;
    public bool IsHunk { get; } = line.Kind == DiffLineKind.Hunk;
    /// <summary>Set on the hunk headers of an uncommitted file that can be staged a hunk at a time.</summary>
    public HunkActions? Hunk { get; } = hunk;
}

public sealed partial class SplitCellViewModel(DiffLine? line, bool left, DiffSyntax syntax) : ObservableObject
{
    public string No { get; } = (left ? line?.OldLine : line?.NewLine)?.ToString() ?? "";
    public string Text { get; } = line?.Text ?? "";
    public IReadOnlyList<DiffSegment>? Segments { get; } = line?.Segments;

    [ObservableProperty]
    private SyntaxSpan[]? _syntax = syntax.For(line);

    public void ApplySyntax(DiffSyntax s) => Syntax = s.For(line);
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

    public void ApplySyntax(DiffSyntax syntax)
    {
        Left.ApplySyntax(syntax);
        Right.ApplySyntax(syntax);
    }
}
