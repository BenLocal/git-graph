using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>How a branch's push/pull state reads in the toolbar and the sidebar, as in VS Code's status bar.</summary>
public static class SyncText
{
    /// <summary>"↓2 ↑1", or empty when there is nothing to pull or push.</summary>
    public static string Counts(SyncStatus? s)
    {
        if (s is null || s.Upstream is null) return "";
        var parts = new List<string>(2);
        if (s.Behind > 0) parts.Add($"↓{s.Behind}");
        if (s.Ahead > 0) parts.Add($"↑{s.Ahead}");
        return string.Join(" ", parts);
    }

    /// <param name="fetchError">Why the last fetch failed, so the counts may be out of date.</param>
    /// <param name="clickable">Adds what clicking does, for the toolbar's sync button.</param>
    public static string Describe(SyncStatus? s, DateTimeOffset? lastFetched, string? fetchError = null, bool clickable = true)
    {
        if (fetchError != null) return $"{Describe(s, lastFetched, null, clickable)}\n\nThe last fetch failed, so this may be out of date:\n{fetchError}".Trim();
        if (s is null) return "";
        if (!s.HasRemote) return "This repository has no remote.";
        if (s.Branch is null) return "Not on a branch (detached HEAD), so there is nothing to pull or push.";
        if (s.Upstream is null)
            return clickable ? $"{s.Branch} isn't on any remote yet. Click to publish it to {s.Remotes[0]}." : $"{s.Branch} isn't on any remote yet.";
        var text = (s.Ahead, s.Behind) switch
        {
            (0, 0) => $"{s.Branch} is up to date with {s.Upstream}.",
            (var a, 0) => $"{Commits(a)} to push to {s.Upstream}.",
            (0, var b) => $"{Commits(b)} to pull from {s.Upstream}.",
            var (a, b) => $"{Commits(b)} to pull from and {Commits(a)} to push to {s.Upstream}.",
        };
        var fetched = lastFetched is { } t ? $" Last fetched {Ago(t)}." : "";
        return clickable ? $"{text}{fetched} Click to sync (pull, then push)." : text + fetched;
    }

    private static string Commits(int n) => n == 1 ? "1 commit" : $"{n} commits";

    private static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        return d.TotalMinutes < 1 ? "just now" : d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes} min ago" : t.ToString("HH:mm");
    }
}
