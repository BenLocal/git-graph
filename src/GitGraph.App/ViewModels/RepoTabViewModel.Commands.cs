using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Graph;

namespace GitGraph.App.ViewModels;

/// <summary>Commands from rows and labels: merge, cherry-pick, revert, reset, rebase, checkout, branches, tags; and the bar for an operation stopped part-way.</summary>
public partial class RepoTabViewModel
{
    // ---- Commands from rows and badges --------------------------------------------------------

    private string? _headBranch;

    /// <summary>What a merge or cherry-pick lands on, for dialog text.</summary>
    private string CurrentLabel => _headBranch ?? "the detached HEAD";

    public async Task MergeAsync(string target, string label)
    {
        var res = await Host.Dialogs.PromptAsync("Merge", $"Merge {label} into {CurrentLabel}.",
            [
                new PromptField("Always create a merge commit, even when a fast-forward is possible", "false", IsCheckbox: true),
                new PromptField("Squash the changes into the working tree without committing", "false", IsCheckbox: true),
            ],
            "Merge");
        if (res is null) return;
        await RunOperationAsync(() => _repo.MergeAsync(target, res[0] == "true", res[1] == "true"));
    }

    public async Task CherryPickAsync(CommitRowViewModel row)
    {
        if (row.IsUncommitted) return;
        var message = row.Commit.IsMerge
            ? $"Apply the changes of merge {row.ShortHash} \"{row.Subject}\" onto {CurrentLabel}, relative to its first parent."
            : $"Apply {row.ShortHash} \"{row.Subject}\" onto {CurrentLabel}.";
        var res = await Host.Dialogs.PromptAsync("Cherry-pick", message,
            [
                new PromptField("Add \"cherry picked from commit …\" to the message", "false", IsCheckbox: true),
                new PromptField("Apply the changes without committing", "false", IsCheckbox: true),
            ],
            "Cherry-pick");
        if (res is null) return;
        await RunOperationAsync(() => _repo.CherryPickAsync(row.Hash, row.Commit.IsMerge, res[0] == "true", res[1] == "true"));
    }

    public async Task RevertAsync(CommitRowViewModel row)
    {
        var message = row.Commit.IsMerge
            ? $"Undo merge {row.ShortHash} \"{row.Subject}\" on {CurrentLabel} with a new commit, relative to its first parent."
            : $"Undo {row.ShortHash} \"{row.Subject}\" on {CurrentLabel} with a new commit.";
        var res = await Host.Dialogs.PromptAsync("Revert", message,
            [new PromptField("Undo the changes without committing", "false", IsCheckbox: true)], "Revert");
        if (res is null) return;
        await RunOperationAsync(() => _repo.RevertAsync(row.Hash, row.Commit.IsMerge, res[0] == "true"));
    }

    public async Task ResetAsync(CommitRowViewModel row, ResetMode mode)
    {
        var (what, destructive) = mode switch
        {
            ResetMode.Soft => ("The commits after it stay as staged changes.", false),
            ResetMode.Mixed => ("The commits after it, and anything staged, stay as unstaged changes.", false),
            _ => ("The commits after it and all uncommitted changes to tracked files are thrown away. This can't be undone from here.", true),
        };
        if (!await Host.Dialogs.ConfirmAsync($"Reset ({mode.ToString().ToLowerInvariant()})",
                $"Move {CurrentLabel} to {row.ShortHash} \"{row.Subject}\". {what}", "Reset", destructive))
            return;
        await RunAsync(() => _repo.ResetAsync(row.Hash, mode));
    }

    public async Task RebaseAsync(string onto, string label)
    {
        var res = await Host.Dialogs.PromptAsync("Rebase",
            $"Replay the commits of {CurrentLabel} that aren't in {label} on top of it. This rewrites those commits: "
            + "if they were pushed already, pushing again needs a force push.",
            [new PromptField("Stash uncommitted changes first and bring them back afterwards", "true", IsCheckbox: true)], "Rebase");
        if (res is null) return;
        await RunOperationAsync(() => _repo.RebaseAsync(onto, res[0] == "true"));
    }

    /// <summary>
    /// Runs a merge, cherry-pick, revert or rebase. If it stops on conflicts, the banner above the graph offers to
    /// continue or abort, and the conflicted files show under the uncommitted changes.
    /// </summary>
    private async Task RunOperationAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (GitException e)
        {
            if (await _repo.GetOperationInProgressAsync() is null) Host.ReportError(e.Message);
            else _showConflicts = true;
        }
        await RefreshAsync();
        await _workspace.RefreshRepoAsync(Path);
    }

    private static string Trim(string message)
    {
        var lines = message.Split('\n');
        return lines.Length <= 8 ? message : string.Join('\n', lines.Take(8)) + "\n…";
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (GitException e) { Host.ReportError(e.Message); }
        await RefreshAsync();
    }

    // ---- An operation stopped part-way ------------------------------------------------------

    /// <summary>A merge, cherry-pick, revert or rebase waiting to be continued or aborted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperation), nameof(OperationText))]
    private GitOperation? _operation;

    // After an operation stops, the uncommitted changes (with the conflicts) are selected once the graph reloads.
    private bool _showConflicts;

    public bool HasOperation => Operation != null;

    public string OperationText => Operation switch
    {
        null => "",
        var op => $"{OperationName(op.Value) switch { var n => char.ToUpperInvariant(n[0]) + n[1..] }} in progress. Resolve the conflicts "
            + "(under Merge conflicts in the uncommitted changes), stage the files, then continue. Or abort to go back to where you were.",
    };

    private static string OperationName(GitOperation op) => op switch
    {
        GitOperation.Merge => "merge",
        GitOperation.CherryPick => "cherry-pick",
        GitOperation.Revert => "revert",
        _ => "rebase",
    };

    [RelayCommand]
    private async Task ContinueOperation()
    {
        if (Operation is not { } op) return;
        await RunOperationAsync(() => _repo.ContinueAsync(op));
    }

    [RelayCommand]
    private async Task AbortOperation()
    {
        if (Operation is not { } op) return;
        var name = OperationName(op);
        if (!await Host.Dialogs.ConfirmAsync($"Abort {name}", $"Stop the {name} and go back to how things were before it started? "
                + "Conflict resolutions made so far are lost.", $"Abort {name}", destructive: true))
            return;
        await RunAsync(() => _repo.AbortAsync(op));
    }

    [RelayCommand]
    private void ShowConflicts()
    {
        if (Rows.FirstOrDefault(r => r.IsUncommitted) is not { } row) return;
        SelectedRow = row;
        Details.SelectedTabIndex = 1;
        if (!Host.ShowDetailsPanel) Host.ShowDetailsPanel = true;
    }

    [RelayCommand]
    private async Task Fetch()
    {
        if (await FetchQuietlyAsync() is { } error) Host.ReportError(error);
        await RefreshAsync();
    }

    /// <summary>The periodic fetch: a failure only shows in the sync tooltip, because a network hiccup isn't worth an error bar.</summary>
    public async Task<string?> AutoFetchAsync()
    {
        var error = await FetchQuietlyAsync();
        await RefreshWhenShownAsync();
        return error;
    }

    private async Task<string?> FetchQuietlyAsync()
    {
        IsSyncing = true;
        try
        {
            await _repo.FetchAsync();
            LastFetched = DateTimeOffset.Now;
            FetchError = null;
        }
        catch (GitException e) { FetchError = Trim(e.Message); }
        finally { IsSyncing = false; }
        return FetchError;
    }

    /// <summary>The sync indicator: publish a branch with no upstream, otherwise pull then push.</summary>
    [RelayCommand]
    private async Task SyncNow()
    {
        if (Sync is not { Branch: { } branch } s || IsSyncing) return;
        if (s.CanPublish)
        {
            if (!await Host.Dialogs.ConfirmAsync("Publish branch", $"Push {branch} to {s.Remotes[0]} and track it there?", "Publish"))
                return;
            await RunSyncAsync(() => _repo.PublishAsync(s.Remotes[0], branch));
            return;
        }
        if (s.Upstream is null) return;
        if (!await Host.Dialogs.ConfirmAsync("Sync",
                $"Pull {(s.Behind > 0 ? $"{s.Behind} commit(s)" : "any new commits")} from {s.Upstream} into {branch}, then push {(s.Ahead > 0 ? $"{s.Ahead} commit(s)" : "anything new")}.",
                "Sync"))
            return;
        await RunSyncAsync(async () =>
        {
            await _repo.PullAsync(Host.PullMode);
            await _repo.PushAsync();
        });
    }

    [RelayCommand]
    private Task Pull() => RunSyncAsync(() => _repo.PullAsync(Host.PullMode));

    [RelayCommand]
    private Task Push() => Sync?.CanPublish == true && Sync.Branch is { } b
        ? RunSyncAsync(() => _repo.PublishAsync(Sync.Remotes[0], b))
        : RunSyncAsync(() => _repo.PushAsync());

    /// <summary>Pushes over a rewritten history (after a rebase or amend), unless someone else pushed since the last fetch.</summary>
    [RelayCommand]
    private async Task ForcePush()
    {
        if (Sync is not { Branch: { } branch, Upstream: { } upstream }) return;
        if (!await Host.Dialogs.ConfirmAsync("Force push",
                $"Replace {upstream} with your {branch}? Commits on {upstream} that aren't in {branch} are dropped. "
                + "The push is refused if someone else pushed to it since your last fetch.",
                "Force push", destructive: true))
            return;
        await RunSyncAsync(() => _repo.PushAsync(forceWithLease: true));
    }

    private async Task RunSyncAsync(Func<Task> action)
    {
        IsSyncing = true;
        try { await RunAsync(action); }
        finally { IsSyncing = false; }
        LastFetched = DateTimeOffset.Now;
        await _workspace.RefreshRepoAsync(Path);
    }

    public async Task CheckoutCommitAsync(CommitRowViewModel row)
    {
        if (row.IsUncommitted) return;
        if (!await Host.Dialogs.ConfirmAsync("Check out commit",
                $"Check out {row.ShortHash} in {Title}? You will be on a detached HEAD until you switch to a branch.", "Check out"))
            return;
        await RunAsync(() => _repo.CheckoutAsync(row.Hash));
        await _workspace.RefreshRepoAsync(Path);
    }

    public async Task CheckoutRefAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { } r) return;
        if (r.Kind == RefKind.RemoteBranch)
        {
            var localName = r.Name[(r.Remote!.Length + 1)..];
            var refs = await _repo.GetRefsAsync();
            await RunAsync(() => refs.Any(x => x.Kind == RefKind.LocalBranch && x.Name == localName)
                ? _repo.CheckoutAsync(localName)
                : GitRunner.RunAsync(Path, ["checkout", "--track", r.Name]));
        }
        else await RunAsync(() => _repo.CheckoutAsync(r.Name));
        await _workspace.RefreshRepoAsync(Path);
    }

    public async Task DeleteRefAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { } r) return;
        if (r.Kind == RefKind.Tag)
        {
            if (!await Host.Dialogs.ConfirmAsync("Delete tag", $"Delete the tag {r.Name}? Remote copies are not touched.", "Delete tag", true)) return;
            await RunAsync(() => _repo.DeleteTagAsync(r.Name));
            return;
        }
        if (!await Host.Dialogs.ConfirmAsync("Delete branch", $"Delete the branch {r.Name}?", "Delete branch", true)) return;
        try
        {
            await _repo.DeleteBranchAsync(r.Name, force: false);
        }
        catch (GitException e) when (e.Message.Contains("not fully merged", StringComparison.OrdinalIgnoreCase))
        {
            if (await Host.Dialogs.ConfirmAsync("Branch is not merged",
                    $"{r.Name} has commits that are not on any other branch. Deleting it may lose them.", "Delete anyway", true))
            {
                try { await _repo.DeleteBranchAsync(r.Name, force: true); }
                catch (GitException e2) { Host.ReportError(e2.Message); }
            }
        }
        catch (GitException e) { Host.ReportError(e.Message); }
        await RefreshAsync();
    }

    public async Task RenameBranchAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { Kind: RefKind.LocalBranch } r) return;
        var res = await Host.Dialogs.PromptAsync("Rename branch", $"Rename the branch {r.Name}. Its remote copies keep their name.",
            [new PromptField("New name", r.Name)], "Rename");
        if (res is null || string.IsNullOrWhiteSpace(res[0]) || res[0].Trim() == r.Name) return;
        await RunAsync(() => _repo.RenameBranchAsync(r.Name, res[0].Trim()));
    }

    public async Task SetUpstreamAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { Kind: RefKind.LocalBranch } r) return;
        var remote = Sync?.Remotes.FirstOrDefault() ?? "origin";
        var res = await Host.Dialogs.PromptAsync("Set upstream",
            $"Track a remote branch with {r.Name}, so pull, push and the ↓↑ counts use it. Fetch first if the remote branch is new.",
            [new PromptField("Remote branch", r.Upstream ?? $"{remote}/{r.Name}")], "Set upstream");
        if (res is null || string.IsNullOrWhiteSpace(res[0])) return;
        await RunAsync(() => _repo.SetUpstreamAsync(r.Name, res[0].Trim()));
    }

    public async Task DeleteRemoteBranchAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { Kind: RefKind.RemoteBranch, Remote: { } remote } r) return;
        var branch = r.Name[(remote.Length + 1)..];
        if (!await Host.Dialogs.ConfirmAsync("Delete remote branch",
                $"Delete {branch} from {remote}? Everyone using {remote} loses it; local branches are not touched.", "Delete from remote", true))
            return;
        await RunSyncAsync(() => _repo.DeleteRemoteBranchAsync(remote, branch));
    }

    public async Task PushTagAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { Kind: RefKind.Tag } r) return;
        var remotes = Sync?.Remotes ?? [];
        if (remotes.Count == 0)
        {
            Host.ReportError("This repository has no remote to push the tag to.");
            return;
        }
        var res = await Host.Dialogs.PromptAsync("Push tag", $"Push the tag {r.Name} to a remote.",
            [new PromptField("Remote", remotes[0], Hint: remotes.Count > 1 ? $"One of {string.Join(", ", remotes)}" : null)], "Push tag");
        if (res is null || string.IsNullOrWhiteSpace(res[0])) return;
        await RunSyncAsync(() => _repo.PushTagAsync(res[0].Trim(), r.Name));
    }

    public async Task CreateBranchAsync(CommitRowViewModel row)
    {
        var res = await Host.Dialogs.PromptAsync("Create branch", $"Start a new branch at {row.ShortHash}.",
            [new PromptField("Branch name", "", "feature/my-change"), new PromptField("Check it out", "true", IsCheckbox: true)],
            "Create branch");
        if (res is null || string.IsNullOrWhiteSpace(res[0])) return;
        await RunAsync(() => _repo.CreateBranchAsync(res[0].Trim(), row.Hash, res[1] == "true"));
        await _workspace.RefreshRepoAsync(Path);
    }

    public async Task CreateTagAsync(CommitRowViewModel row)
    {
        var res = await Host.Dialogs.PromptAsync("Create tag", $"Tag {row.ShortHash}.",
            [new PromptField("Tag name", "", "v1.0.0"), new PromptField("Message", "", "Leave empty for a lightweight tag")],
            "Create tag");
        if (res is null || string.IsNullOrWhiteSpace(res[0])) return;
        await RunAsync(() => _repo.CreateTagAsync(res[0].Trim(), row.Hash, res[1]));
    }

    public async Task AddWorktreeAtAsync(CommitRowViewModel row, string? branch)
    {
        var main = _worktrees.FirstOrDefault(w => w.IsMain)?.Path ?? Path;
        await WorktreeActions.AddAsync(_workspace, main, System.IO.Path.GetFileName(main), row.IsUncommitted ? "HEAD" : row.Hash, branch);
        await RefreshAsync();
    }

    public async Task AddWorktreeForBranchAsync(RefBadgeViewModel badge)
    {
        if (badge.Ref is not { } r) return;
        var main = _worktrees.FirstOrDefault(w => w.IsMain)?.Path ?? Path;
        if (r.Kind == RefKind.RemoteBranch)
        {
            await WorktreeActions.AddAsync(_workspace, main, System.IO.Path.GetFileName(main), r.Name, null);
        }
        else await WorktreeActions.AddAsync(_workspace, main, System.IO.Path.GetFileName(main), r.Name, r.Name);
        await RefreshAsync();
    }

    public void OpenWorktreeByName(string name)
    {
        var wt = _worktrees.FirstOrDefault(w => System.IO.Path.GetFileName(w.Path) == name);
        if (wt != null) _workspace.OpenTab(wt.Path, pin: true);
    }
}
