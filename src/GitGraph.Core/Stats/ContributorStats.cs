namespace GitGraph.Core.Stats;

/// <summary>One non-merge commit for contributor statistics. Line counts are null until they have been counted.</summary>
public sealed record CommitStat(string Hash, string Name, string Email, DateTimeOffset When, int? Additions, int? Deletions);

public sealed record ContributorSummary(
    string Name,
    string Email,
    int Commits,
    int Additions,
    int Deletions,
    DateTimeOffset First,
    DateTimeOffset Last,
    IReadOnlyList<int> WeeklyCommits);

public sealed record ContributorReport(
    int TotalCommits,
    int TotalAdditions,
    int TotalDeletions,
    IReadOnlyList<DateTimeOffset> Weeks,
    IReadOnlyList<int> WeeklyCommits,
    IReadOnlyList<ContributorSummary> Contributors,
    IReadOnlyDictionary<DateOnly, int> DailyCommits);

/// <summary>
/// Aggregates commits per person and per week, like GitHub's Contributors page: one entry per email
/// (after .mailmap), weeks starting on Sunday, from the first week in range up to the current week.
/// </summary>
public static class ContributorStats
{
    public static ContributorReport Build(IReadOnlyList<CommitStat> commits, DateTimeOffset? since, DateTimeOffset now)
    {
        var inRange = since is { } s ? commits.Where(c => c.When >= s).ToList() : commits.ToList();
        if (inRange.Count == 0) return new ContributorReport(0, 0, 0, [], [], [], new Dictionary<DateOnly, int>());

        var firstWeek = WeekStart(since ?? inRange.Min(c => c.When));
        var lastWeek = WeekStart(now);
        var weeks = new List<DateTimeOffset>();
        for (var w = firstWeek; w <= lastWeek; w = w.AddDays(7)) weeks.Add(w);
        int WeekIndex(DateTimeOffset when) => Math.Clamp((int)((WeekStart(when) - firstWeek).TotalDays / 7), 0, weeks.Count - 1);

        var total = new int[weeks.Count];
        foreach (var c in inRange) total[WeekIndex(c.When)]++;

        var people = inRange
            .GroupBy(c => c.Email.Trim().ToLowerInvariant())
            .Select(g =>
            {
                var weekly = new int[weeks.Count];
                foreach (var c in g) weekly[WeekIndex(c.When)]++;
                var latest = g.MaxBy(c => c.When)!;
                return new ContributorSummary(latest.Name, latest.Email, g.Count(), g.Sum(c => c.Additions ?? 0), g.Sum(c => c.Deletions ?? 0),
                    g.Min(c => c.When), latest.When, weekly);
            })
            .OrderByDescending(p => p.Commits)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Days in the commit's own time zone, as people experienced them.
        var daily = inRange.GroupBy(c => DateOnly.FromDateTime(c.When.DateTime)).ToDictionary(g => g.Key, g => g.Count());
        return new ContributorReport(inRange.Count, inRange.Sum(c => c.Additions ?? 0), inRange.Sum(c => c.Deletions ?? 0), weeks, total, people, daily);
    }

    /// <summary>The Sunday 00:00 (in the time's own offset) that starts its week.</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset t)
    {
        var day = new DateTimeOffset(t.Year, t.Month, t.Day, 0, 0, 0, t.Offset);
        return day.AddDays(-(int)day.DayOfWeek);
    }
}
