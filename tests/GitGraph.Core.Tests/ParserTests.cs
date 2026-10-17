using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.Core.Tests;

public class ParserTests
{
    [Fact]
    public void ParseLog_reads_fields_and_parents()
    {
        var output = "aaa\0bbb ccc\0Ann\0ann@x\01700000000\0Merge it\x1e\nbbb\0\0Bob\0bob@x\01600000000\0First\x1e\n";
        var log = GitParsers.ParseLog(output);

        Assert.Equal(2, log.Count);
        Assert.Equal(["bbb", "ccc"], log[0].Parents);
        Assert.True(log[0].IsMerge);
        Assert.Empty(log[1].Parents);
        Assert.Equal("First", log[1].Subject);
        Assert.Equal(1600000000, log[1].AuthorDate.ToUnixTimeSeconds());
    }

    [Fact]
    public void ParseRefs_peels_annotated_tags_and_skips_remote_head()
    {
        var output =
            "refs/heads/main\0h1\0\0origin/main\0*\n" +
            "refs/remotes/origin/HEAD\0h1\0\0\0 \n" +
            "refs/remotes/origin/main\0h1\0\0\0 \n" +
            "refs/tags/v1\0tagobj\0h0\0\0 \n";
        var refs = GitParsers.ParseRefs(output);

        Assert.Equal(3, refs.Count);
        Assert.True(refs[0].IsHead);
        Assert.Equal("origin/main", refs[0].Upstream);
        Assert.Equal("origin", refs[1].Remote);
        Assert.Equal("h0", refs[2].Target);
    }

    [Fact]
    public void ParseWorktrees_reads_main_and_linked_entries()
    {
        var porcelain = """
            worktree /src/app
            HEAD 111
            branch refs/heads/main

            worktree /src/app-feature
            HEAD 222
            detached
            locked

            """;
        var wts = GitParsers.ParseWorktrees(porcelain);

        Assert.Equal(2, wts.Count);
        Assert.True(wts[0].IsMain);
        Assert.Equal("main", wts[0].BranchShortName);
        Assert.False(wts[1].IsMain);
        Assert.True(wts[1].IsDetached);
        Assert.True(wts[1].IsLocked);
    }

    [Fact]
    public void ParseChanges_merges_rename_and_numstat()
    {
        var nameStatus = "M\0src/a.cs\0R087\0old.txt\0new.txt\0A\0img.png\0";
        var numstat = "3\t1\tsrc/a.cs\0" + "2\t2\t\0old.txt\0new.txt\0" + "-\t-\timg.png\0";
        var changes = GitParsers.ParseChanges(nameStatus, numstat);

        Assert.Equal(3, changes.Count);
        Assert.Equal((3, 1), (changes[0].Additions, changes[0].Deletions));
        Assert.Equal(ChangeKind.Renamed, changes[1].Kind);
        Assert.Equal("old.txt", changes[1].OldPath);
        Assert.Equal(2, changes[1].Additions);
        Assert.True(changes[2].IsBinary);
    }

    [Fact]
    public void TextDiff_numbers_lines_and_pairs_split_rows()
    {
        var diff = TextDiff.Build("keep\nold value\ntail\n", "keep\nnew value\nextra\ntail\n");

        Assert.Equal(1, diff.Deletions);
        Assert.Equal(2, diff.Additions);
        Assert.Equal("@@ -1,3 +1,4 @@", diff.Lines[0].Text);
        // Unified: deletions before additions, as git prints them.
        Assert.Equal(
            [DiffLineKind.Hunk, DiffLineKind.Context, DiffLineKind.Deleted, DiffLineKind.Added, DiffLineKind.Added, DiffLineKind.Context],
            diff.Lines.Select(l => l.Kind));
        Assert.Equal([2, 3], diff.Lines.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.NewLine!.Value));
        Assert.Equal(3, diff.Lines.Single(l => l.Text == "tail").OldLine);

        // Split: hunk, keep, old|new, (empty)|extra, tail
        Assert.Equal(5, diff.SplitRows.Count);
        Assert.Equal("old value", diff.SplitRows[2].Left!.Text);
        Assert.Equal("new value", diff.SplitRows[2].Right!.Text);
        Assert.Null(diff.SplitRows[3].Left);
    }

    [Fact]
    public void TextDiff_marks_changed_words_in_modified_lines()
    {
        var diff = TextDiff.Build("var x = 1;\n", "var x = 2;\n");
        var deleted = diff.Lines.Single(l => l.Kind == DiffLineKind.Deleted);
        var added = diff.Lines.Single(l => l.Kind == DiffLineKind.Added);

        Assert.NotNull(added.Segments);
        Assert.Equal("var x = 1;", string.Concat(deleted.Segments!.Select(s => s.Text)));
        Assert.Contains(added.Segments!, s => s.Changed && s.Text.Contains('2'));
        Assert.DoesNotContain(added.Segments!, s => s.Changed && s.Text.Contains("var"));
    }

    [Fact]
    public void TextDiff_collapses_unchanged_lines_into_hunks()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"line {i}").ToList();
        var oldText = string.Join('\n', lines) + "\n";
        lines[1] = "changed 2";
        lines[27] = "changed 28";
        var diff = TextDiff.Build(oldText, string.Join('\n', lines) + "\n");

        var hunks = diff.Lines.Where(l => l.Kind == DiffLineKind.Hunk).Select(l => l.Text).ToList();
        Assert.Equal(["@@ -1,5 +1,5 @@", "@@ -25,6 +25,6 @@"], hunks);
    }

    [Fact]
    public void TextDiff_of_an_added_file_has_no_deletions()
    {
        var diff = TextDiff.Build("", "a\nb\n");
        Assert.Equal((2, 0), (diff.Additions, diff.Deletions));
        Assert.Equal("@@ -0,0 +1,2 @@", diff.Lines[0].Text);
    }

    [Fact]
    public void TextDiff_sections_fold_long_unchanged_stretches()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"line {i}").ToList();
        var oldText = string.Join('\n', lines);
        lines[14] = "changed 15";
        var diff = TextDiff.Build(oldText, string.Join('\n', lines));

        Assert.Equal(30, diff.AllRows.Count);
        // Rows 0-10 fold, 11-17 show (the change at 14 with 3 rows each side), 18-29 fold.
        Assert.Equal([(0, 11, true), (11, 7, false), (18, 12, true)], TextDiff.Sections(diff.AllRows));
        // Unchanged stretches shorter than minFold stay shown.
        Assert.Equal([(0, 30, false)], TextDiff.Sections(diff.AllRows, context: 3, minFold: 20));
    }

    [Fact]
    public void TextDiff_reports_binary_content()
    {
        Assert.True(TextDiff.Build("a\0b", "a").IsBinary);
    }
}
