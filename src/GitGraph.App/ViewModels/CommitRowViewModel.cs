using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Controls;
using GitGraph.Core.Git;
using GitGraph.Core.Graph;

namespace GitGraph.App.ViewModels;

public partial class CommitRowViewModel : ObservableObject
{
    private readonly RepoTabViewModel _owner;

    public CommitRowViewModel(RepoTabViewModel owner, CommitInfo commit, GraphRow graph, bool isHead, bool isUncommitted)
    {
        _owner = owner;
        Commit = commit;
        Graph = graph;
        IsHead = isHead;
        IsUncommitted = isUncommitted;
    }

    public CommitInfo Commit { get; }
    public GraphRow Graph { get; }
    public bool IsHead { get; }
    public bool IsUncommitted { get; }
    /// <summary>Set on a stash's row, which hangs off the commit the stash was made on.</summary>
    public StashEntry? Stash { get; init; }
    public bool IsStash => Stash != null;
    /// <summary>A real commit: not the uncommitted changes, not a stash.</summary>
    public bool IsCommit => !IsUncommitted && !IsStash;
    public List<RefBadgeViewModel> Badges { get; } = [];

    public string Hash => Commit.Hash;
    public string ShortHash => IsUncommitted ? "" : Commit.ShortHash;
    public string Subject => Commit.Subject;
    public string Author => IsUncommitted ? "" : Commit.AuthorName;
    public string? AuthorEmail => IsUncommitted ? null : Commit.AuthorEmail;
    public string DateText => IsUncommitted ? "" : Commit.AuthorDate.ToString("yyyy-MM-dd HH:mm");
    public string Tooltip => IsUncommitted ? Subject
        : Stash is { } st ? $"{st.Name}: {st.Message}\n{Commit.AuthorName}, {DateText}"
        : $"{Commit.Hash}\n{Commit.AuthorName} <{Commit.AuthorEmail}>";
    public bool HasBadges => Badges.Count > 0;
    public IBrush LaneBrush => LanePalette.Brush(Graph.NodeColor);

    [ObservableProperty]
    private bool _isMatch;

    [ObservableProperty]
    private bool _isCompareTarget;

    /// <summary>Review comments on the commit's diff.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComments), nameof(CommentTip))]
    private int _commentCount;

    public bool HasComments => CommentCount > 0;
    public string CommentTip => CommentCount == 1 ? "1 comment on this commit's changes" : $"{CommentCount} comments on this commit's changes";

    /// <summary>Faded because another branch is highlighted.</summary>
    [ObservableProperty]
    private bool _isDimmed;

    [RelayCommand]
    private Task CopyHash() => _owner.Host.Dialogs.CopyTextAsync(Commit.Hash);

    [RelayCommand]
    private Task CopySubject() => _owner.Host.Dialogs.CopyTextAsync(Commit.Subject);

    [RelayCommand]
    private Task Checkout() => _owner.CheckoutCommitAsync(this);

    [RelayCommand]
    private Task CreateBranch() => _owner.CreateBranchAsync(this);

    [RelayCommand]
    private Task CreateTag() => _owner.CreateTagAsync(this);

    [RelayCommand]
    private Task AddWorktree() => _owner.AddWorktreeAtAsync(this, null);

    [RelayCommand]
    private void CompareWithSelected() => _owner.SetCompare(this);

    [RelayCommand]
    private Task Merge() => _owner.MergeAsync(Commit.Hash, $"{Commit.ShortHash} \"{Commit.Subject}\"");

    [RelayCommand]
    private Task CherryPick() => _owner.CherryPickAsync(this);

    [RelayCommand]
    private Task Revert() => _owner.RevertAsync(this);

    /// <param name="mode">Soft, Mixed or Hard.</param>
    [RelayCommand]
    private Task Reset(string mode) => _owner.ResetAsync(this, Enum.Parse<ResetMode>(mode));

    [RelayCommand]
    private Task Rebase() => _owner.RebaseAsync(Commit.Hash, $"{Commit.ShortHash} \"{Commit.Subject}\"");

    [RelayCommand]
    private Task InteractiveRebase() => _owner.InteractiveRebaseAsync(this);

    [RelayCommand]
    private Task FoldStagedInto() => _owner.FoldStagedIntoAsync(this);

    /// <summary>The user's custom commands for a commit, read when the menu opens.</summary>
    public IReadOnlyList<CustomCommandItem> CustomCommands => IsCommit
        ? _owner.Commands.For("Commit", () => new() { ["sha"] = Hash, ["short"] = ShortHash, ["subject"] = Subject, ["branch"] = _owner.HeadBranch ?? "" })
        : [];

    /// <summary>Merging or picking HEAD onto itself does nothing, so those menu items hide there.</summary>
    public bool CanApply => IsCommit && !IsHead;

    [RelayCommand]
    private Task StashChanges() => _owner.StashChangesCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task ApplyStash() => _owner.ApplyStashAsync(Stash!, pop: false);

    [RelayCommand]
    private Task PopStash() => _owner.ApplyStashAsync(Stash!, pop: true);

    [RelayCommand]
    private Task DropStash() => _owner.DropStashAsync(Stash!);

    [RelayCommand]
    private Task StashBranch() => _owner.StashBranchAsync(Stash!);

    [RelayCommand]
    private Task CopyStashName() => _owner.Host.Dialogs.CopyTextAsync(Stash!.Name);
}

public enum BadgeKind { Local, Remote, Tag, Worktree, Stash }

public partial class RefBadgeViewModel : ObservableObject
{
    private readonly RepoTabViewModel _owner;

    public RefBadgeViewModel(RepoTabViewModel owner, BadgeKind kind, string text, int color, GitRef? gitRef, bool isCurrent)
    {
        _owner = owner;
        Kind = kind;
        Text = text;
        Ref = gitRef;
        IsCurrent = isCurrent;
        LaneBrush = LanePalette.Brush(color);
    }

    public BadgeKind Kind { get; }
    public string Text { get; }
    public GitRef? Ref { get; }
    public bool IsCurrent { get; }
    public IBrush LaneBrush { get; }

    public bool IsLocal => Kind == BadgeKind.Local;
    public bool IsRemote => Kind == BadgeKind.Remote;
    public bool IsTag => Kind == BadgeKind.Tag;
    public bool IsWorktree => Kind == BadgeKind.Worktree;
    public bool IsStash => Kind == BadgeKind.Stash;
    public bool IsBranch => Kind is BadgeKind.Local or BadgeKind.Remote;

    /// <summary>Remotes whose branch of the same name points here too, shown inside this label.</summary>
    public List<string> Remotes { get; } = [];
    public bool HasRemotes => Remotes.Count > 0;
    public bool CanDelete => Kind is BadgeKind.Local or BadgeKind.Tag && !IsCurrent;
    /// <summary>The menu ends with a delete item: here, or on the remote.</summary>
    public bool HasDeleteItem => CanDelete || IsRemote;

    public string Tooltip => Kind switch
    {
        BadgeKind.Local when IsCurrent => $"{Text}, checked out here",
        BadgeKind.Local when Remotes.Count > 0 => $"Branch {Text}, the same commit as {string.Join(" and ", Remotes.Select(r => $"{r}/{Text}"))}",
        BadgeKind.Local when Ref?.Upstream is { } up => $"Branch {Text}, tracking {up}",
        BadgeKind.Local => $"Branch {Text}",
        BadgeKind.Remote => $"Remote branch {Text}",
        BadgeKind.Tag => $"Tag {Text}",
        BadgeKind.Stash => $"Stash {Text}. Right-click the row to apply, pop or drop it.",
        _ => $"Checked out in worktree {Text}",
    };

    [RelayCommand]
    private Task Checkout() => _owner.CheckoutRefAsync(this);

    [RelayCommand]
    private void Highlight() => _owner.HighlightBranch(this);

    public bool CanHighlight => Ref != null;

    /// <summary>The user's custom commands for a branch or tag, read when the menu opens.</summary>
    public IReadOnlyList<CustomCommandItem> CustomCommands => Ref is { } r
        ? _owner.Commands.For("Branch", () => new() { ["branch"] = r.Name, ["sha"] = r.Target, ["short"] = r.Target[..7] })
        : [];

    [RelayCommand]
    private Task Merge() => _owner.MergeAsync(Ref!.Name, Text);

    public bool CanMerge => IsBranch && !IsCurrent;

    [RelayCommand]
    private Task Rebase() => _owner.RebaseAsync(Ref!.Name, Text);

    [RelayCommand]
    private Task Delete() => _owner.DeleteRefAsync(this);

    [RelayCommand]
    private Task Rename() => _owner.RenameBranchAsync(this);

    [RelayCommand]
    private Task SetUpstream() => _owner.SetUpstreamAsync(this);

    [RelayCommand]
    private Task DeleteRemote() => _owner.DeleteRemoteBranchAsync(this);

    [RelayCommand]
    private Task PushTag() => _owner.PushTagAsync(this);

    [RelayCommand]
    private Task AddWorktree() => _owner.AddWorktreeForBranchAsync(this);

    [RelayCommand]
    private void OpenWorktree() => _owner.OpenWorktreeByName(Text);

    [RelayCommand]
    private Task CopyName() => _owner.Host.Dialogs.CopyTextAsync(Text);
}
