using System.Collections;
using GitGraph.Core.Stats;

namespace GitGraph.Core.Tests;

public sealed class ContributorStatsTests
{
    [Fact]
    public void Cancellation_stops_aggregation_while_reading_commits()
    {
        using var cts = new CancellationTokenSource();
        var commits = new CancellingCommits(cts);
        Assert.Throws<OperationCanceledException>(() => ContributorStats.Build(commits, null, DateTimeOffset.UtcNow, cts.Token));
        Assert.Equal(1, commits.ReadCount);
    }

    private sealed class CancellingCommits(CancellationTokenSource cts) : IReadOnlyList<CommitStat>
    {
        public int ReadCount { get; private set; }
        public int Count => 100;
        public CommitStat this[int index] => new(index.ToString(), "Tester", "test@example.com", DateTimeOffset.UtcNow, 1, 0);

        public IEnumerator<CommitStat> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                ReadCount++;
                cts.Cancel();
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
