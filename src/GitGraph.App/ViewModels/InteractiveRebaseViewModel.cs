using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>
/// An interactive rebase planned in a list, as in Fork: each commit after the base can be picked, reworded, squashed or
/// fixed up into the one before it, or dropped, and the order changed by dragging. Newest first, like the graph.
/// </summary>
public sealed partial class InteractiveRebaseViewModel : ObservableObject
{
    private readonly RepoTabViewModel _tab;

    /// <param name="commits">Oldest first, as git replays them.</param>
    public InteractiveRebaseViewModel(RepoTabViewModel tab, CommitInfo onto, IReadOnlyList<RebaseCommit> commits, string branch, int pushed)
    {
        _tab = tab;
        Onto = onto;
        foreach (var c in commits.Reverse())
        {
            var row = new RebaseRowViewModel(c, this);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }
        Rows.CollectionChanged += (_, _) => Validate();
        Summary = $"{commits.Count} {(commits.Count == 1 ? "commit" : "commits")} on {branch}, replayed onto {onto.ShortHash} \"{onto.Subject}\".";
        PushedWarning = pushed == 0 ? null
            : $"{pushed} of them {(pushed == 1 ? "is" : "are")} already pushed: the branch will need a force push, and anyone who has them will see a different history.";
        Validate();
    }

    public CommitInfo Onto { get; }
    public string Title => _tab.Title;
    public string Summary { get; }
    public string? PushedWarning { get; }

    /// <summary>Newest first, as in the graph; the rebase replays them from the bottom up.</summary>
    public ObservableCollection<RebaseRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private RebaseRowViewModel? _selected;

    /// <summary>Why the rebase can't start as planned; null when it can.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string? _problem;

    /// <summary>Raised when the window should close: the rebase started, or was cancelled.</summary>
    public event Action? CloseRequested;

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RebaseRowViewModel.Action) or nameof(RebaseRowViewModel.Message)) Validate();
    }

    private void Validate()
    {
        var kept = Rows.Reverse().Where(r => r.Action != RebaseAction.Drop).ToList();
        Problem = kept.Count == 0 ? "Every commit is dropped. Keep at least one, or reset the branch instead."
            : kept[0].Action is RebaseAction.Squash or RebaseAction.Fixup
                ? $"\"{kept[0].Subject}\" is the oldest commit kept, so there is no earlier one to fold it into. Pick it instead."
            : kept.FirstOrDefault(r => r.Action == RebaseAction.Reword && string.IsNullOrWhiteSpace(r.Message)) is { } empty
                ? $"The new message for \"{empty.Subject}\" is empty."
            : null;
    }

    public void Move(RebaseRowViewModel row, int index)
    {
        var from = Rows.IndexOf(row);
        index = Math.Clamp(index, 0, Rows.Count - 1);
        if (from < 0 || from == index) return;
        Rows.Move(from, index);
        Selected = row;
    }

    [RelayCommand]
    private void MoveUp(RebaseRowViewModel row) => Move(row, Rows.IndexOf(row) - 1);

    [RelayCommand]
    private void MoveDown(RebaseRowViewModel row) => Move(row, Rows.IndexOf(row) + 1);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        var steps = Rows.Reverse().Select(r => new RebaseStep(r.Commit.Hash, r.Action, r.Action == RebaseAction.Reword ? r.Message : null)).ToList();
        CloseRequested?.Invoke();
        await _tab.RunInteractiveRebaseAsync(Onto.Hash, steps);
    }

    private bool CanStart() => Problem is null;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}

public sealed partial class RebaseRowViewModel(RebaseCommit commit, InteractiveRebaseViewModel owner) : ObservableObject
{
    public static IReadOnlyList<RebaseAction> Actions { get; } = Enum.GetValues<RebaseAction>();

    public InteractiveRebaseViewModel Owner { get; } = owner;
    public CommitInfo Commit { get; } = commit.Commit;
    public string ShortHash => Commit.ShortHash;
    public string Subject => Commit.Subject;
    public string Meta { get; } = $"{commit.Commit.AuthorName}, {commit.Commit.AuthorDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReword), nameof(IsDropped), nameof(IsFolded), nameof(ActionTip))]
    private RebaseAction _action = RebaseAction.Pick;

    /// <summary>The whole message, edited when the commit is reworded.</summary>
    [ObservableProperty]
    private string _message = commit.Message;

    public bool IsReword => Action == RebaseAction.Reword;
    public bool IsDropped => Action == RebaseAction.Drop;
    /// <summary>Squash and fixup fold the commit into the one below it (the one before it in time).</summary>
    public bool IsFolded => Action is RebaseAction.Squash or RebaseAction.Fixup;

    public string ActionTip => Action switch
    {
        RebaseAction.Pick => "Keep the commit as it is",
        RebaseAction.Reword => "Keep the changes, with the message edited below",
        RebaseAction.Squash => "Fold into the commit below it, keeping both messages",
        RebaseAction.Fixup => "Fold into the commit below it, dropping this message",
        _ => "Leave the commit out",
    };
}
