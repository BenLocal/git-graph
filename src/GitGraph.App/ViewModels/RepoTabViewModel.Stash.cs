using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Graph;

namespace GitGraph.App.ViewModels;

/// <summary>Stashing the uncommitted changes, and applying, popping, dropping or branching a stash.</summary>
public partial class RepoTabViewModel
{
    // ---- Stash -------------------------------------------------------------------------------

    [RelayCommand]
    private async Task StashChanges()
    {
        var r = await Host.Dialogs.PromptAsync("Stash changes",
            "Save the uncommitted changes in a stash and clean the working tree. The stash shows in the graph above the commit it was made on; apply or pop it from there.",
            [new PromptField("Message", "", "Optional, to tell stashes apart"), new PromptField("Include untracked files", "true", IsCheckbox: true)],
            "Stash");
        if (r is null) return;
        await RunAsync(() => _repo.StashAsync(r[0], r[1] == "true"));
    }

    [RelayCommand]
    private async Task PopLatestStash()
    {
        if (_stashes.Count == 0)
        {
            Host.ReportError("There are no stashes to pop.");
            return;
        }
        await RunAsync(() => _repo.ApplyStashAsync(_stashes[0].Name, pop: true, restoreIndex: false));
    }

    /// <param name="pop">Drop the stash after it applied cleanly; on conflicts git keeps it.</param>
    public Task ApplyStashAsync(StashEntry stash, bool pop) => RunAsync(() => _repo.ApplyStashAsync(stash.Name, pop, restoreIndex: false));

    public async Task DropStashAsync(StashEntry stash)
    {
        if (!await Host.Dialogs.ConfirmAsync("Drop stash", $"Delete {stash.Name} (\"{stash.Message}\")? Its changes are lost.", "Drop stash", destructive: true))
            return;
        await RunAsync(() => _repo.DropStashAsync(stash.Name));
    }

    public async Task StashBranchAsync(StashEntry stash)
    {
        var r = await Host.Dialogs.PromptAsync("Create branch from stash",
            $"Create a branch at the commit {stash.Name} was made on, check it out, and apply the stash there. The stash is dropped if it applies cleanly.",
            [new PromptField("Branch name", "", "e.g. feature/resume-work")], "Create branch");
        if (r is null || string.IsNullOrWhiteSpace(r[0])) return;
        await RunAsync(() => _repo.StashBranchAsync(r[0].Trim(), stash.Name));
    }
}
