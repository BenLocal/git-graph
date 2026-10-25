using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>
/// One commit's review comments, shared by the details panel and the diff windows opened from it, so a comment made
/// in one shows in the others. Adding, editing and deleting ask for the text and write it to git notes.
/// </summary>
public sealed partial class CommitComments(GitRepository repo, IAppHost host, string commit) : ObservableObject
{
    public string Commit { get; } = commit;

    /// <summary>Every comment on the commit, in the order they were first made.</summary>
    [ObservableProperty]
    private IReadOnlyList<CodeComment> _all = [];

    // Who comments are made as: only their own comments can be edited or deleted.
    private string? _email;

    public bool IsMine(CodeComment c) => _email != null && string.Equals(c.Email, _email, StringComparison.OrdinalIgnoreCase);

    public IEnumerable<CodeComment> For(string file) => All.Where(c => c.File == file);

    /// <summary>
    /// Those of a file's <paramref name="comments"/> on a diff line, which is <paramref name="oldLine"/> in the parent
    /// and <paramref name="newLine"/> in the commit (either is null when the line is only on the other side).
    /// </summary>
    public IReadOnlyList<CommentViewModel> At(IReadOnlyList<CodeComment> comments, int? oldLine, int? newLine)
    {
        if (comments.Count == 0) return [];
        List<CommentViewModel>? at = null;
        foreach (var c in comments)
            if (c.Line == (c.OldSide ? oldLine : newLine)) (at ??= []).Add(new CommentViewModel(c, this));
        return at ?? (IReadOnlyList<CommentViewModel>)[];
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            _email ??= (await repo.GetIdentityAsync(ct)).Email;
            var all = await repo.GetCommentsAsync(Commit, ct);
            if (!ct.IsCancellationRequested) All = all;
        }
        catch (OperationCanceledException) { }
        catch (GitException e) { host.ReportError(e.Message); }
    }

    public async Task AddAsync(string file, bool oldSide, int line)
    {
        var r = await host.Dialogs.PromptAsync("Comment on this line",
            $"{file}, line {line}{(oldSide ? " before the change" : "")}. Comments are kept in git notes and pushed with the branch.",
            [new PromptField("Comment", "", "Ctrl+Enter to save", IsMultiline: true)], "Comment");
        if (r is null || string.IsNullOrWhiteSpace(r[0])) return;
        await ChangeAsync(() => repo.AddCommentAsync(Commit, file, oldSide, line, r[0].Trim()));
    }

    public async Task EditAsync(CodeComment comment)
    {
        var r = await host.Dialogs.PromptAsync("Edit comment", $"{comment.File}, line {comment.Line}.",
            [new PromptField("Comment", comment.Text, "Ctrl+Enter to save", IsMultiline: true)], "Save");
        if (r is null || string.IsNullOrWhiteSpace(r[0]) || r[0].Trim() == comment.Text) return;
        await ChangeAsync(() => repo.EditCommentAsync(Commit, comment, r[0].Trim()));
    }

    public async Task DeleteAsync(CodeComment comment)
    {
        if (!await host.Dialogs.ConfirmAsync("Delete comment", $"Delete your comment on {comment.File}, line {comment.Line}?", "Delete", destructive: true))
            return;
        await ChangeAsync(() => repo.DeleteCommentAsync(Commit, comment));
    }

    private async Task ChangeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (GitException e)
        {
            host.ReportError(e.Message);
            return;
        }
        await LoadAsync();
    }
}

/// <summary>A comment as shown under its line, with Edit and Delete for the user's own.</summary>
public sealed partial class CommentViewModel(CodeComment comment, CommitComments owner)
{
    public CodeComment Comment { get; } = comment;
    public string Author { get; } = comment.Author;
    public string Email { get; } = comment.Email;
    public string DateText { get; } = comment.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string Text { get; } = comment.Text;
    public bool IsMine { get; } = owner.IsMine(comment);
    public string LineText { get; } = comment.OldSide ? $"Line {comment.Line}, before" : $"Line {comment.Line}";

    [RelayCommand]
    private Task Edit() => owner.EditAsync(Comment);

    [RelayCommand]
    private Task Delete() => owner.DeleteAsync(Comment);
}

/// <summary>A diff line that can take a comment: the line in the commit's file, or in its parent's for a deleted line.</summary>
public sealed partial class CommentSpot(CommitComments owner, string file, bool oldSide, int line)
{
    public bool OldSide { get; } = oldSide;
    public int Line { get; } = line;

    [RelayCommand]
    private Task Add() => owner.AddAsync(file, OldSide, Line);
}
