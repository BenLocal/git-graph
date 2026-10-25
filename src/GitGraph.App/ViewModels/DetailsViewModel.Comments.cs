using System.ComponentModel;

namespace GitGraph.App.ViewModels;

/// <summary>Review comments on the lines of a commit's diff.</summary>
public partial class DetailsViewModel
{
    // The selected commit's comments; null for anything else (uncommitted changes, a stash, a comparison), whose
    // lines can't take comments.
    private CommitComments? _comments;
    private CancellationTokenSource? _commentsCts;

    /// <summary>Called with a commit and how many comments it now has, each time they are loaded or changed.</summary>
    public Action<string, int>? CommentsChanged { get; set; }

    private void LoadComments(string? commit)
    {
        _commentsCts?.Cancel();
        if (_comments != null) _comments.PropertyChanged -= OnCommentsChanged;
        _comments = commit is null ? null : new CommitComments(_repo, _host, commit);
        ShowComments();
        if (_comments is null) return;
        _comments.PropertyChanged += OnCommentsChanged;
        var cts = _commentsCts = new CancellationTokenSource();
        _ = Loads.Track(_comments.LoadAsync(cts.Token));
    }

    private void OnCommentsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CommitComments c || c != _comments) return;
        ShowComments();
        CommentsChanged?.Invoke(c.Commit, c.All.Count);
    }

    /// <summary>The comments a file's lines can take: the selected commit's, for one of its own files.</summary>
    private CommitComments? CommentsFor(FileChangeViewModel? file) =>
        _comments is { } c && file is { Group: FileGroup.Commit } && file.Target.To == c.Commit ? c : null;

    /// <summary>The comments of the file on screen, under their lines.</summary>
    private void ShowComments()
    {
        var file = SelectedFile;
        var owner = CommentsFor(file);
        var path = file?.Change.Path ?? "";
        var comments = owner?.For(path).ToList() ?? [];
        foreach (var line in DiffLines) line.ShowComments(owner, path, comments);
        foreach (var row in SplitRows) row.ShowComments(owner, path, comments);
    }
}
