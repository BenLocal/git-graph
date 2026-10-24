using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Stats;

namespace GitGraph.App.ViewModels;

public sealed record PeriodOption(string Label, int? Months)
{
    public override string ToString() => Label;
}

public sealed record SortOption(string Label, Func<ContributorSummary, int> Key)
{
    public override string ToString() => Label;
}

/// <summary>
/// Who contributed to the checked-out branch, like GitHub's Insights → Contributors: weekly commits for
/// the whole repository, then one card per person, over a chosen period.
/// </summary>
public sealed partial class RepoStatsViewModel(GitRepository repo) : ObservableObject
{
    private List<CommitStat>? _commits;
    private CancellationTokenSource? _cts;

    public IReadOnlyList<PeriodOption> Periods { get; } =
    [
        new("All time", null),
        new("Last 12 months", 12),
        new("Last 6 months", 6),
        new("Last 3 months", 3),
        new("Last month", 1),
    ];

    public IReadOnlyList<SortOption> Sorts { get; } =
    [
        new("Commits", p => p.Commits),
        new("Additions", p => p.Additions),
        new("Deletions", p => p.Deletions),
    ];

    [ObservableProperty]
    private PeriodOption _period = null!;

    [ObservableProperty]
    private SortOption _sort = null!;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Commits are shown; lines added and removed are still being counted.</summary>
    [ObservableProperty]
    private bool _isCountingLines;

    /// <summary>"Showing the top 100 of 201 people", when not everyone gets a card.</summary>
    [ObservableProperty]
    private string? _moreNote;

    [ObservableProperty]
    private IReadOnlyDictionary<DateOnly, int> _dailyCommits = new Dictionary<DateOnly, int>();

    private const int MaxCards = 100;

    /// <summary>Where the heatmap starts: the period's start, or the first commit for all time.</summary>
    [ObservableProperty]
    private DateTimeOffset? _heatmapFrom;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string? _branch;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    private IReadOnlyList<int> _weeklyCommits = [];

    [ObservableProperty]
    private IReadOnlyList<DateTimeOffset> _weeks = [];

    /// <summary>The busiest week of any one person: every card's chart uses this scale, so heights compare.</summary>
    [ObservableProperty]
    private double _personMaximum;

    public ObservableCollection<ContributorCardViewModel> Contributors { get; } = [];

    partial void OnPeriodChanged(PeriodOption value) => _ = RebuildAsync();
    partial void OnSortChanged(SortOption value) => _ = RebuildAsync();

    /// <summary>
    /// Reads the history the first time the view opens, and again after HEAD moved. Commits and people show
    /// right away; lines added and removed are counted afterwards (git has to diff every commit) and cached on
    /// disk, so later visits only count new commits.
    /// </summary>
    public async Task EnsureLoadedAsync(string? branch)
    {
        Branch = branch;
        if (_commits != null || IsLoading) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsLoading = true;
        Message = null;
        try
        {
            var commitsTask = repo.GetCommitStatsAsync(withLines: false, ct: cts.Token);
            var commonDir = await repo.GetCommonDirAsync(cts.Token) ?? repo.Path;
            var cache = await Task.Run(() => StatsCache.Load(commonDir), cts.Token);
            var commits = await commitsTask;
            if (cts.IsCancellationRequested) return;

            var missing = 0;
            for (var i = 0; i < commits.Count; i++)
            {
                if (cache.Lines.TryGetValue(commits[i].Hash, out var l)) commits[i] = commits[i] with { Additions = l.Additions, Deletions = l.Deletions };
                else missing++;
            }
            _commits = commits;
            Period ??= Periods[0];
            Sort ??= Sorts[0];
            IsLoading = false;
            IsCountingLines = missing > 0;
            await RebuildAsync();

            if (missing > 0) await CountLinesAsync(cache, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (GitException e) { Message = $"Could not read the history: {e.Message}"; }
        finally
        {
            if (_cts == cts)
            {
                IsLoading = false;
                IsCountingLines = false;
            }
        }
    }

    /// <summary>The slow pass: line counts for commits not reachable from heads counted before.</summary>
    private async Task CountLinesAsync(StatsCache cache, CancellationToken ct)
    {
        var counted = await repo.GetCommitStatsAsync(withLines: true, exclude: cache.Heads, ct);
        // A remembered head may be gone (branch deleted and collected); then count everything once.
        if (counted.Count == 0 && cache.Heads.Count > 0) counted = await repo.GetCommitStatsAsync(withLines: true, ct: ct);
        if (ct.IsCancellationRequested || _commits is null) return;
        foreach (var c in counted) cache.Lines[c.Hash] = (c.Additions ?? 0, c.Deletions ?? 0);
        if (await repo.GetHeadAsync(ct) is { Hash: { } head }) cache.AddHead(head);
        await Task.Run(cache.Save, ct);

        for (var i = 0; i < _commits.Count; i++)
            if (_commits[i].Additions is null && cache.Lines.TryGetValue(_commits[i].Hash, out var l))
                _commits[i] = _commits[i] with { Additions = l.Additions, Deletions = l.Deletions };
        IsCountingLines = false;
        await RebuildAsync();
    }

    /// <summary>New commits arrived; the next look reads the history again.</summary>
    public void Invalidate()
    {
        _cts?.Cancel();
        _commits = null;
        IsLoading = false;
    }

    /// <summary>Aggregates in the background (large histories have many thousands of commits), then shows the result.</summary>
    private async Task RebuildAsync()
    {
        if (_commits is null || Period is null || Sort is null) return;
        var commits = _commits.ToList();
        var now = DateTimeOffset.Now;
        var since = Period.Months is { } m ? now.AddMonths(-m) : (DateTimeOffset?)null;
        var sort = Sort;
        var report = await Task.Run(() => ContributorStats.Build(commits, since, now));
        var ranked = report.Contributors.OrderByDescending(sort.Key).ThenByDescending(p => p.Commits).ToList();

        Weeks = report.Weeks;
        WeeklyCommits = report.WeeklyCommits;
        DailyCommits = report.DailyCommits;
        HeatmapFrom = since ?? (report.Weeks.Count > 0 ? report.Weeks[0] : null);
        PersonMaximum = ranked.Count == 0 ? 1 : Math.Max(1, ranked.Take(MaxCards).Max(c => c.WeeklyCommits.Max()));
        var people = ranked.Count;
        Summary = report.TotalCommits == 0
            ? null
            : $"{report.TotalCommits:N0} {(report.TotalCommits == 1 ? "commit" : "commits")} by {people:N0} {(people == 1 ? "person" : "people")}" +
              (IsCountingLines ? ". Counting lines added and removed…" : $", {report.TotalAdditions:N0} lines added and {report.TotalDeletions:N0} removed");
        Message = report.TotalCommits == 0 && !IsLoading
            ? (_commits.Count == 0 ? "No commits on this branch yet." : "No commits in this period. Choose a longer one.")
            : null;
        MoreNote = people > MaxCards ? $"Showing the top {MaxCards} of {people:N0} people." : null;

        // Up to 100 cards took over a second to lay out in one go, freezing the window; a few at a time, with layout
        // and drawing in between, the top ones show at once and the window keeps responding. A newer rebuild stops this one.
        var version = ++_fillVersion;
        Contributors.Clear();
        var rank = 0;
        foreach (var p in ranked.Take(MaxCards))
        {
            Contributors.Add(new ContributorCardViewModel(p, ++rank, report.TotalCommits, IsCountingLines));
            if (rank % CardsPerPass != 0) continue;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (version != _fillVersion) return;
        }
    }

    private const int CardsPerPass = 4;
    private int _fillVersion;
}

public sealed class ContributorCardViewModel(ContributorSummary person, int rank, int totalCommits, bool countingLines)
{
    public bool HasLines { get; } = !countingLines;
    public bool IsCountingLines { get; } = countingLines;
    public string Name { get; } = person.Name;
    public string Email { get; } = person.Email;
    public string Rank { get; } = $"#{rank}";
    public IReadOnlyList<int> Weekly { get; } = person.WeeklyCommits;
    public string CommitsText { get; } = $"{person.Commits:N0} {(person.Commits == 1 ? "commit" : "commits")}";
    public string AdditionsText { get; } = $"{person.Additions:N0} ++";
    public string DeletionsText { get; } = $"{person.Deletions:N0} --";
    public double Share { get; } = totalCommits == 0 ? 0 : (double)person.Commits / totalCommits;
    public string ShareText => $"{Share:P0} of commits";
    public string Active { get; } = person.First.Date == person.Last.Date
        ? person.First.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
        : $"{person.First.ToString("MMM yyyy", CultureInfo.InvariantCulture)} – {person.Last.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";

    /// <summary>Each person draws in one of the graph's lane colours, by rank, like lines on the transit map.</summary>
    public IBrush Brush { get; } = LanePalette.Brush(rank - 1);
}
