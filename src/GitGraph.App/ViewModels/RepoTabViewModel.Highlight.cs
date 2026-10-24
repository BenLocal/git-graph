using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>One branch's commits stand out and the rest dim, like GitKraken's Solo, to follow a line through a busy graph.</summary>
public partial class RepoTabViewModel
{
    // The highlighted ref by full name, so it survives refreshes that move it; the refs of the last build.
    private string? _highlightRef;
    private List<GitRef> _refs = [];

    /// <summary>The note above the graph while a branch is highlighted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHighlighting))]
    private string? _highlightText;

    public bool IsHighlighting => HighlightText != null;

    public void HighlightBranch(RefBadgeViewModel badge)
    {
        if (badge.Ref is null) return;
        _highlightRef = badge.Ref.FullName;
        ApplyHighlight();
    }

    [RelayCommand]
    public void ClearHighlight()
    {
        _highlightRef = null;
        ApplyHighlight();
    }

    /// <summary>Escape leaves both comparing and highlighting.</summary>
    [RelayCommand]
    private void ClearMarks()
    {
        ClearCompare();
        ClearHighlight();
    }

    /// <summary>Marks every loaded row the highlighted ref doesn't lead to; called again after each rebuild.</summary>
    private void ApplyHighlight()
    {
        var target = _highlightRef is null ? null : _refs.FirstOrDefault(r => r.FullName == _highlightRef);
        if (target is null)
        {
            // Never highlighted, cleared, or the branch is gone.
            _highlightRef = null;
            foreach (var r in Rows) r.IsDimmed = false;
            HighlightText = null;
            return;
        }
        // The ref's commit and everything it reaches through parents, among the rows loaded.
        var byHash = Rows.Where(r => !r.IsUncommitted).ToDictionary(r => r.Hash);
        var reached = new HashSet<string>();
        var next = new Stack<string>([target.Target]);
        while (next.TryPop(out var hash))
        {
            if (!reached.Add(hash) || !byHash.TryGetValue(hash, out var row)) continue;
            foreach (var parent in row.Commit.Parents) next.Push(parent);
        }
        var count = 0;
        foreach (var r in Rows)
        {
            // The uncommitted changes belong to the branch checked out.
            r.IsDimmed = r.IsUncommitted ? !target.IsHead : !reached.Contains(r.Hash);
            if (!r.IsDimmed && !r.IsUncommitted) count++;
        }
        HighlightText = $"Highlighting {target.Name}: {count} of the {byHash.Count} commits loaded lead to it.";
    }
}
