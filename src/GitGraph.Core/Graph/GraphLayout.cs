namespace GitGraph.Core.Graph;

public enum SegmentKind
{
    /// <summary>A lane passing straight through the row, top to bottom.</summary>
    Pass,
    /// <summary>From a lane at the top edge into the row's node.</summary>
    In,
    /// <summary>From the row's node to a lane at the bottom edge.</summary>
    Out,
}

public readonly record struct GraphSegment(SegmentKind Kind, int FromLane, int ToLane, int Color, bool Dashed);

public sealed class GraphRow
{
    public required int NodeLane { get; init; }
    public required int NodeColor { get; init; }
    public required IReadOnlyList<GraphSegment> Segments { get; init; }
    /// <summary>Number of lanes this row touches, for sizing the graph column.</summary>
    public required int LaneCount { get; init; }
    public bool IsMerge { get; init; }
    public bool NodeDashed { get; init; }
}

public readonly record struct GraphInput(string Hash, IReadOnlyList<string> Parents, bool Dashed = false);

/// <summary>
/// Assigns every commit a lane and records, per row, the line segments needed to draw it.
/// Input must be ordered children-before-parents (git log's --date-order or --topo-order).
/// Lanes keep their column for their whole life, so each row can be drawn on its own.
/// </summary>
public static class GraphLayout
{
    private sealed class Lane(string hash, int color, bool dashed)
    {
        public string Hash = hash;
        public int Color = color;
        public bool Dashed = dashed;
    }

    public static List<GraphRow> Build(IReadOnlyList<GraphInput> commits)
    {
        var rows = new List<GraphRow>(commits.Count);
        var lanes = new List<Lane?>();
        var nextColor = 0;

        foreach (var c in commits)
        {
            var before = lanes.Count;
            var col = -1;
            for (var i = 0; i < lanes.Count; i++)
                if (lanes[i]?.Hash == c.Hash) { col = i; break; }

            int color;
            if (col < 0)
            {
                col = FreeSlot(lanes, -1);
                color = nextColor++;
                Set(lanes, col, null);
            }
            else color = lanes[col]!.Color;

            var segs = new List<GraphSegment>();
            for (var i = 0; i < lanes.Count; i++)
            {
                var l = lanes[i];
                if (l is null) continue;
                if (l.Hash == c.Hash)
                {
                    segs.Add(new GraphSegment(SegmentKind.In, i, col, l.Color, l.Dashed));
                    if (i != col) lanes[i] = null; // converged into this commit
                }
                else segs.Add(new GraphSegment(SegmentKind.Pass, i, i, l.Color, l.Dashed));
            }

            if (c.Parents.Count == 0)
            {
                lanes[col] = null;
            }
            else
            {
                lanes[col] = new Lane(c.Parents[0], color, c.Dashed);
                segs.Add(new GraphSegment(SegmentKind.Out, col, col, color, c.Dashed));
                for (var p = 1; p < c.Parents.Count; p++)
                {
                    var target = -1;
                    for (var i = 0; i < lanes.Count; i++)
                        if (lanes[i]?.Hash == c.Parents[p]) { target = i; break; }
                    if (target < 0)
                    {
                        target = FreeSlot(lanes, col);
                        Set(lanes, target, new Lane(c.Parents[p], nextColor++, false));
                    }
                    segs.Add(new GraphSegment(SegmentKind.Out, col, target, lanes[target]!.Color, false));
                }
            }

            var width = Math.Max(before, lanes.Count);
            while (lanes.Count > 0 && lanes[^1] is null) lanes.RemoveAt(lanes.Count - 1);

            rows.Add(new GraphRow
            {
                NodeLane = col,
                NodeColor = color,
                Segments = segs,
                LaneCount = Math.Max(width, col + 1),
                IsMerge = c.Parents.Count > 1,
                NodeDashed = c.Dashed,
            });
        }
        return rows;
    }

    private static int FreeSlot(List<Lane?> lanes, int exclude)
    {
        for (var i = 0; i < lanes.Count; i++)
            if (lanes[i] is null && i != exclude) return i;
        return lanes.Count;
    }

    private static void Set(List<Lane?> lanes, int index, Lane? lane)
    {
        while (lanes.Count <= index) lanes.Add(null);
        lanes[index] = lane;
    }
}
