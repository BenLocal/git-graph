using System.Text.RegularExpressions;

namespace GitGraph.Core.Diff;

public enum DiffLineKind { Context, Added, Deleted, Hunk, Note }

public sealed record DiffLine(DiffLineKind Kind, int? OldLine, int? NewLine, string Text);

/// <summary>One row of a side-by-side view. Either side may be empty when a block is unbalanced.</summary>
public sealed record SplitDiffRow(DiffLine? Left, DiffLine? Right, bool IsHunk);

public sealed class ParsedDiff
{
    public required IReadOnlyList<DiffLine> Lines { get; init; }
    public bool IsBinary { get; init; }
    public int Additions { get; init; }
    public int Deletions { get; init; }
}

public static partial class UnifiedDiff
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@(.*)$")]
    private static partial Regex HunkHeader();

    public static ParsedDiff Parse(string diff, int tabSize = 4)
    {
        var lines = new List<DiffLine>();
        int oldNo = 0, newNo = 0, adds = 0, dels = 0;
        var inHunk = false;
        var binary = false;
        var tab = new string(' ', tabSize);

        foreach (var raw in diff.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            var m = HunkHeader().Match(line);
            if (m.Success)
            {
                oldNo = int.Parse(m.Groups[1].Value);
                newNo = int.Parse(m.Groups[2].Value);
                inHunk = true;
                lines.Add(new DiffLine(DiffLineKind.Hunk, null, null, line));
                continue;
            }
            if (!inHunk)
            {
                if (line.StartsWith("Binary files ", StringComparison.Ordinal)) binary = true;
                continue;
            }
            if (line.Length == 0)
            {
                // Trailing newline at the end of the output; a real empty context line is " ".
                continue;
            }
            var text = line[1..].Replace("\t", tab);
            switch (line[0])
            {
                case ' ': lines.Add(new DiffLine(DiffLineKind.Context, oldNo++, newNo++, text)); break;
                case '+': lines.Add(new DiffLine(DiffLineKind.Added, null, newNo++, text)); adds++; break;
                case '-': lines.Add(new DiffLine(DiffLineKind.Deleted, oldNo++, null, text)); dels++; break;
                case '\\': lines.Add(new DiffLine(DiffLineKind.Note, null, null, line)); break;
                default: inHunk = false; break; // start of the next file's header
            }
        }
        return new ParsedDiff { Lines = lines, IsBinary = binary, Additions = adds, Deletions = dels };
    }

    /// <summary>Pairs each run of deletions with the run of additions that follows it.</summary>
    public static List<SplitDiffRow> ToSplit(IReadOnlyList<DiffLine> lines)
    {
        var rows = new List<SplitDiffRow>();
        var dels = new List<DiffLine>();
        var adds = new List<DiffLine>();

        void FlushBlock()
        {
            var n = Math.Max(dels.Count, adds.Count);
            for (var i = 0; i < n; i++)
                rows.Add(new SplitDiffRow(i < dels.Count ? dels[i] : null, i < adds.Count ? adds[i] : null, false));
            dels.Clear();
            adds.Clear();
        }

        foreach (var l in lines)
        {
            switch (l.Kind)
            {
                case DiffLineKind.Deleted:
                    if (adds.Count > 0) FlushBlock();
                    dels.Add(l);
                    break;
                case DiffLineKind.Added:
                    adds.Add(l);
                    break;
                case DiffLineKind.Note:
                    break;
                case DiffLineKind.Hunk:
                    FlushBlock();
                    rows.Add(new SplitDiffRow(l, l, true));
                    break;
                default:
                    FlushBlock();
                    rows.Add(new SplitDiffRow(l, l, false));
                    break;
            }
        }
        FlushBlock();
        return rows;
    }
}
