using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace GitGraph.Core.Diff;

public enum DiffLineKind { Context, Added, Deleted, Hunk }

/// <summary>A run of text in a changed line; <see cref="Changed"/> marks the words that differ from the other side.</summary>
public sealed record DiffSegment(string Text, bool Changed);

/// <summary>One diff line. <see cref="Segments"/> is set only for modified lines that have word-level changes.</summary>
public sealed record DiffLine(DiffLineKind Kind, int? OldLine, int? NewLine, string Text, IReadOnlyList<DiffSegment>? Segments = null);

/// <summary>One row of a side-by-side view. Either side is null where the other side has lines with no counterpart.</summary>
public sealed record SplitDiffRow(DiffLine? Left, DiffLine? Right, bool IsHunk);

public sealed class FileDiff
{
    public required IReadOnlyList<DiffLine> Lines { get; init; }
    public required IReadOnlyList<SplitDiffRow> SplitRows { get; init; }
    /// <summary>Every row of the file side by side, unchanged ones included, with no hunk headers.</summary>
    public IReadOnlyList<SplitDiffRow> AllRows { get; init; } = [];
    public bool IsBinary { get; init; }
    public int Additions { get; init; }
    public int Deletions { get; init; }
}

/// <summary>
/// Diffs two versions of a file with DiffPlex: lines first, then words inside modified lines.
/// Unchanged stretches are collapsed to <c>context</c> lines around each change, like git's hunks.
/// </summary>
public static class TextDiff
{
    public static FileDiff Build(string oldText, string newText, int context = 3, int tabSize = 4)
    {
        if (oldText.Contains('\0') || newText.Contains('\0'))
            return new FileDiff { Lines = [], SplitRows = [], IsBinary = true };

        // ignoreWhiteSpace defaults to true in DiffPlex; a whitespace-only change must still show.
        var model = SideBySideDiffBuilder.Diff(Differ.Instance, TrimFinalNewline(oldText), TrimFinalNewline(newText),
            ignoreWhiteSpace: false, ignoreCase: false, LineChunker.Instance, WordChunker.Instance);
        var tab = new string(' ', tabSize);

        // DiffPlex pads both sides to the same length, so row i of each side lines up.
        var rows = new List<(DiffLine? Old, DiffLine? New)>();
        int adds = 0, dels = 0;
        for (var i = 0; i < model.OldText.Lines.Count; i++)
        {
            var o = model.OldText.Lines[i];
            var n = model.NewText.Lines[i];
            if (o.Type == ChangeType.Unchanged && n.Type == ChangeType.Unchanged)
            {
                rows.Add((Line(DiffLineKind.Context, o, n, tab), Line(DiffLineKind.Context, o, n, tab)));
                continue;
            }
            var del = o.Type == ChangeType.Imaginary ? null : Line(DiffLineKind.Deleted, o, null, tab);
            var add = n.Type == ChangeType.Imaginary ? null : Line(DiffLineKind.Added, null, n, tab);
            if (del != null) dels++;
            if (add != null) adds++;
            rows.Add((del, add));
        }

        var lines = new List<DiffLine>();
        var split = new List<SplitDiffRow>();
        foreach (var (start, end) in Hunks(rows, context))
        {
            var header = HunkHeader(rows, start, end);
            lines.Add(header);
            split.Add(new SplitDiffRow(header, header, true));

            // Unified view: within a block of changes, all deletions come before the additions, as in git.
            var pendingAdds = new List<DiffLine>();
            for (var i = start; i < end; i++)
            {
                var (o, n) = rows[i];
                split.Add(new SplitDiffRow(o, n, false));
                if (o?.Kind == DiffLineKind.Context)
                {
                    lines.AddRange(pendingAdds);
                    pendingAdds.Clear();
                    lines.Add(o);
                    continue;
                }
                if (o != null) lines.Add(o);
                if (n != null) pendingAdds.Add(n);
            }
            lines.AddRange(pendingAdds);
        }

        return new FileDiff
        {
            Lines = lines,
            SplitRows = split,
            AllRows = rows.Select(r => new SplitDiffRow(r.Old, r.New, false)).ToList(),
            Additions = adds,
            Deletions = dels,
        };
    }

    /// <summary>The newline that ends the last line would otherwise show up as an extra empty line.</summary>
    private static string TrimFinalNewline(string text) =>
        text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;

    private static DiffLine Line(DiffLineKind kind, DiffPiece? oldPiece, DiffPiece? newPiece, string tab)
    {
        var piece = oldPiece ?? newPiece!;
        var text = (piece.Text ?? "").Replace("\t", tab);
        IReadOnlyList<DiffSegment>? segments = null;
        if (piece.Type == ChangeType.Modified && piece.SubPieces.Count > 0)
        {
            segments = piece.SubPieces
                .Where(s => s.Type != ChangeType.Imaginary && !string.IsNullOrEmpty(s.Text))
                .Select(s => new DiffSegment(s.Text!.Replace("\t", tab), s.Type != ChangeType.Unchanged))
                .ToList();
        }
        return new DiffLine(kind, oldPiece?.Position, newPiece?.Position, text, segments);
    }

    /// <summary>
    /// Splits <see cref="FileDiff.AllRows"/> into shown and foldable stretches: changes with <paramref name="context"/>
    /// rows around them stay shown, and unchanged stretches of at least <paramref name="minFold"/> rows can be folded.
    /// </summary>
    public static List<(int Start, int Count, bool Foldable)> Sections(IReadOnlyList<SplitDiffRow> rows, int context = 3, int minFold = 4)
    {
        var shown = new bool[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Left?.Kind == DiffLineKind.Context) continue;
            for (var j = Math.Max(0, i - context); j <= Math.Min(rows.Count - 1, i + context); j++) shown[j] = true;
        }
        var sections = new List<(int Start, int Count, bool Foldable)>();
        var start = 0;
        for (var i = 1; i <= rows.Count; i++)
        {
            if (i < rows.Count && shown[i] == shown[start]) continue;
            var count = i - start;
            var foldable = !shown[start] && count >= minFold;
            // A short unchanged stretch isn't worth a fold; show it with its neighbours.
            if (sections.Count > 0 && !sections[^1].Foldable && !foldable)
                sections[^1] = (sections[^1].Start, sections[^1].Count + count, false);
            else
                sections.Add((start, count, foldable));
            start = i;
        }
        return sections;
    }

    /// <summary>Row ranges [start, end) that contain changes plus their surrounding context, merged when they touch.</summary>
    private static List<(int Start, int End)> Hunks(List<(DiffLine? Old, DiffLine? New)> rows, int context)
    {
        var hunks = new List<(int Start, int End)>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Old?.Kind == DiffLineKind.Context) continue;
            var start = Math.Max(0, i - context);
            var end = Math.Min(rows.Count, i + context + 1);
            if (hunks.Count > 0 && start <= hunks[^1].End)
                hunks[^1] = (hunks[^1].Start, Math.Max(hunks[^1].End, end));
            else
                hunks.Add((start, end));
        }
        return hunks;
    }

    private static DiffLine HunkHeader(List<(DiffLine? Old, DiffLine? New)> rows, int start, int end)
    {
        int? oldStart = null, newStart = null;
        int oldCount = 0, newCount = 0;
        for (var i = start; i < end; i++)
        {
            if (rows[i].Old is { OldLine: { } o }) { oldStart ??= o; oldCount++; }
            if (rows[i].New is { NewLine: { } n }) { newStart ??= n; newCount++; }
        }
        // git numbers an empty side from the line before it, which is 0 at the top of the file.
        var text = $"@@ -{oldStart ?? PrecedingLine(rows, start, old: true)},{oldCount} +{newStart ?? PrecedingLine(rows, start, old: false)},{newCount} @@";
        return new DiffLine(DiffLineKind.Hunk, null, null, text);
    }

    private static int PrecedingLine(List<(DiffLine? Old, DiffLine? New)> rows, int start, bool old)
    {
        for (var i = start - 1; i >= 0; i--)
        {
            var no = old ? rows[i].Old?.OldLine : rows[i].New?.NewLine;
            if (no != null) return no.Value;
        }
        return 0;
    }
}
