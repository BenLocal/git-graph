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
    public List<RefBadgeViewModel> Badges { get; } = [];

    public string Hash => Commit.Hash;
    public string ShortHash => IsUncommitted ? "" : Commit.ShortHash;
    public string Subject => Commit.Subject;
    public string Author => IsUncommitted ? "" : Commit.AuthorName;
    public string DateText => IsUncommitted ? "" : Commit.AuthorDate.ToString("yyyy-MM-dd HH:mm");
    public string Tooltip => IsUncommitted ? Subject : $"{Commit.Hash}\n{Commit.AuthorName} <{Commit.AuthorEmail}>";
    public bool HasBadges => Badges.Count > 0;
    public IBrush LaneBrush => LanePalette.Brush(Graph.NodeColor);

    [ObservableProperty]
    private bool _isMatch;

    [ObservableProperty]
    private bool _isCompareTarget;

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
}

public enum BadgeKind { Local, Remote, Tag, Worktree }

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
    public bool IsBranch => Kind is BadgeKind.Local or BadgeKind.Remote;
    public bool CanDelete => Kind is BadgeKind.Local or BadgeKind.Tag && !IsCurrent;

    public string Tooltip => Kind switch
    {
        BadgeKind.Local when IsCurrent => $"{Text}, checked out here",
        BadgeKind.Local when Ref?.Upstream is { } up => $"Branch {Text}, tracking {up}",
        BadgeKind.Local => $"Branch {Text}",
        BadgeKind.Remote => $"Remote branch {Text}",
        BadgeKind.Tag => $"Tag {Text}",
        _ => $"Checked out in worktree {Text}",
    };

    [RelayCommand]
    private Task Checkout() => _owner.CheckoutRefAsync(this);

    [RelayCommand]
    private Task Delete() => _owner.DeleteRefAsync(this);

    [RelayCommand]
    private Task AddWorktree() => _owner.AddWorktreeForBranchAsync(this);

    [RelayCommand]
    private void OpenWorktree() => _owner.OpenWorktreeByName(Text);

    [RelayCommand]
    private Task CopyName() => _owner.Host.Dialogs.CopyTextAsync(Text);
}
