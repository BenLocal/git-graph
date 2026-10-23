using System.Text;
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

/// <summary>
/// A run of <see cref="FileDiff.AllRows"/>: unchanged on both sides, or one change with its lines on each side (either
/// side may have none). Starts are 0-based line indices in that side's file.
/// </summary>
public sealed record DiffBlock(bool Changed, int OldStart, int OldCount, int NewStart, int NewCount);

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
    /// <param name="ignoreWhitespace">Lines that differ only in whitespace count as unchanged.</param>
    public static FileDiff Build(string oldText, string newText, int context = 3, int tabSize = 4, bool ignoreWhitespace = false)
    {
        if (oldText.Contains('\0') || newText.Contains('\0'))
            return new FileDiff { Lines = [], SplitRows = [], IsBinary = true };

        // ignoreWhiteSpace defaults to true in DiffPlex; unless asked, a whitespace-only change must still show.
        var model = SideBySideDiffBuilder.Diff(Differ.Instance, TrimFinalNewline(oldText), TrimFinalNewline(newText),
            ignoreWhiteSpace: ignoreWhitespace, ignoreCase: false, LineChunker.Instance, WordChunker.Instance);
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
                // Each side shows its own text: with whitespace ignored, the two can differ.
                rows.Add((Line(DiffLineKind.Context, o, n, tab), Line(DiffLineKind.Context, o, n, tab, textOf: n)));
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

    /// <summary>
    /// A patch that makes only hunk number <paramref name="hunk"/> of <paramref name="diff"/> (built from these same
    /// texts), for <c>git apply --recount</c>. Lines come from the original texts rather than the displayed ones,
    /// so tabs and line endings survive.
    /// </summary>
    public static string HunkPatch(string path, string oldText, string newText, FileDiff diff, int hunk)
    {
        var (oldLines, oldEndsWithNewline) = RawLines(oldText);
        var (newLines, newEndsWithNewline) = RawLines(newText);
        var sb = new StringBuilder();
        sb.Append($"diff --git a/{path} b/{path}\n--- a/{path}\n+++ b/{path}\n");

        void Emit(char sign, string text, bool noNewline)
        {
            sb.Append(sign).Append(text).Append('\n');
            if (noNewline) sb.Append("\\ No newline at end of file\n");
        }

        var index = -1;
        foreach (var line in diff.Lines)
        {
            if (line.Kind == DiffLineKind.Hunk)
            {
                if (++index == hunk) sb.Append(line.Text).Append('\n');
                continue;
            }
            if (index != hunk) continue;
            // The last line of a file without a final newline needs git's marker after it.
            var oldLast = line.OldLine == oldLines.Length && !oldEndsWithNewline;
            var newLast = line.NewLine == newLines.Length && !newEndsWithNewline;
            switch (line.Kind)
            {
                case DiffLineKind.Context when oldLast == newLast:
                    Emit(' ', oldLines[line.OldLine!.Value - 1], oldLast);
                    break;
                case DiffLineKind.Context:
                    // Same text, but only one side ends with a newline: to git that is a changed line.
                    Emit('-', oldLines[line.OldLine!.Value - 1], oldLast);
                    Emit('+', newLines[line.NewLine!.Value - 1], newLast);
                    break;
                case DiffLineKind.Deleted:
                    Emit('-', oldLines[line.OldLine!.Value - 1], oldLast);
                    break;
                case DiffLineKind.Added:
                    Emit('+', newLines[line.NewLine!.Value - 1], newLast);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Lines as stored, a CR of a CRLF ending included, without splitting on the final newline.</summary>
    private static (string[] Lines, bool EndsWithNewline) RawLines(string text)
    {
        if (text.Length == 0) return ([], true);
        var endsWithNewline = text.EndsWith('\n');
        return ((endsWithNewline ? text[..^1] : text).Split('\n'), endsWithNewline);
    }

    /// <summary>The newline that ends the last line would otherwise show up as an extra empty line.</summary>
    private static string TrimFinalNewline(string text) =>
        text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;

    /// <param name="textOf">The piece whose text to show, when not the old one.</param>
    private static DiffLine Line(DiffLineKind kind, DiffPiece? oldPiece, DiffPiece? newPiece, string tab, DiffPiece? textOf = null)
    {
        var piece = textOf ?? oldPiece ?? newPiece!;
        var text = (piece.Text ?? "").Replace("\t", tab);
        IReadOnlyList<DiffSegment>? segments = null;
        if (piece.Type == ChangeType.Modified && piece.SubPieces.Count > 0)
        {
            segments = piece.SubPieces
                .Where(s => s.Type != ChangeType.Imaginary && !string.IsNullOrEmpty(s.Text))
                // A changed run of only whitespace reads as noise, so it isn't marked.
                .Select(s => new DiffSegment(s.Text!.Replace("\t", tab), s.Type != ChangeType.Unchanged && !string.IsNullOrWhiteSpace(s.Text)))
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

    /// <summary>Splits <see cref="FileDiff.AllRows"/> into alternating unchanged and changed blocks.</summary>
    public static List<DiffBlock> Blocks(IReadOnlyList<SplitDiffRow> rows)
    {
        var blocks = new List<DiffBlock>();
        int oldLine = 0, newLine = 0;
        foreach (var row in rows)
        {
            var changed = row.Left?.Kind != DiffLineKind.Context;
            int o = row.Left != null ? 1 : 0, n = row.Right != null ? 1 : 0;
            if (blocks.Count > 0 && blocks[^1].Changed == changed)
                blocks[^1] = blocks[^1] with { OldCount = blocks[^1].OldCount + o, NewCount = blocks[^1].NewCount + n };
            else
                blocks.Add(new DiffBlock(changed, oldLine, o, newLine, n));
            oldLine += o;
            newLine += n;
        }
        return blocks;
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
