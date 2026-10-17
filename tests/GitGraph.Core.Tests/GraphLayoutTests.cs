using GitGraph.Core.Graph;

namespace GitGraph.Core.Tests;

public class GraphLayoutTests
{
    private static GraphInput C(string hash, params string[] parents) => new(hash, parents);

    [Fact]
    public void Linear_history_stays_in_lane_zero()
    {
        var rows = GraphLayout.Build([C("c", "b"), C("b", "a"), C("a")]);

        Assert.All(rows, r => Assert.Equal(0, r.NodeLane));
        Assert.All(rows, r => Assert.Equal(1, r.LaneCount));
        Assert.DoesNotContain(rows[2].Segments, s => s.Kind == SegmentKind.Out);
        Assert.Contains(rows[1].Segments, s => s.Kind == SegmentKind.In && s.FromLane == 0);
    }

    [Fact]
    public void Merge_opens_a_lane_for_the_second_parent_and_closes_it_at_the_fork()
    {
        //   m        merge of a1 (main) and f1 (feature)
        //   |\
        //   | f1
        //   a1 |
        //   |/
        //   base
        var rows = GraphLayout.Build([C("m", "a1", "f1"), C("f1", "base"), C("a1", "base"), C("base")]);

        Assert.True(rows[0].IsMerge);
        Assert.Contains(rows[0].Segments, s => s is { Kind: SegmentKind.Out, FromLane: 0, ToLane: 1 });
        Assert.Equal(1, rows[1].NodeLane);
        Assert.Equal(0, rows[2].NodeLane);
        // Both lanes wait for "base" and converge into its node.
        Assert.Equal(0, rows[3].NodeLane);
        Assert.Contains(rows[3].Segments, s => s is { Kind: SegmentKind.In, FromLane: 1, ToLane: 0 });
        Assert.Equal(2, rows[3].LaneCount);
    }

    [Fact]
    public void Lane_keeps_its_color_along_first_parents()
    {
        var rows = GraphLayout.Build([C("m", "a1", "f1"), C("f1", "base"), C("a1", "base"), C("base")]);

        Assert.Equal(rows[0].NodeColor, rows[2].NodeColor);
        Assert.NotEqual(rows[0].NodeColor, rows[1].NodeColor);
    }

    [Fact]
    public void Freed_lane_is_reused_by_a_later_branch()
    {
        var rows = GraphLayout.Build([
            C("x", "a"), C("y", "a"), C("a", "b"), // x and y both fork from a
            C("z", "b"), C("b"),                    // z starts after lane 1 was freed
        ]);

        Assert.Equal(1, rows[1].NodeLane);
        Assert.Equal(1, rows[3].NodeLane);
    }

    [Fact]
    public void Dashed_commit_marks_its_lane_until_the_parent()
    {
        var rows = GraphLayout.Build([new GraphInput("*", ["b"], Dashed: true), C("c", "b"), C("b")]);

        Assert.True(rows[0].NodeDashed);
        Assert.Contains(rows[1].Segments, s => s.Dashed && s.Kind == SegmentKind.Pass);
        Assert.Contains(rows[2].Segments, s => s.Dashed && s.Kind == SegmentKind.In);
    }

    [Fact]
    public void Parent_missing_from_the_page_leaves_an_open_lane()
    {
        var rows = GraphLayout.Build([C("b", "a")]);

        Assert.Contains(rows[0].Segments, s => s.Kind == SegmentKind.Out);
    }
}
