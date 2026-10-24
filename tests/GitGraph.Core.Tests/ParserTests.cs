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
    public void ParseReflog_splits_the_action_from_its_message()
    {
        var output = "aaaa\0HEAD@{1700000000}\0commit (amend): Fix: the parser\x1e\n" +
                     "bbbb\0HEAD@{1699999000}\0checkout: moving from main to feat\x1e\n" +
                     "cccc\0HEAD@{1699998000}\0rebase (finish)\x1e\n";
        var log = GitParsers.ParseReflog(output);
        Assert.Equal(["commit (amend)", "checkout", "rebase (finish)"], log.Select(e => e.Action));
        // Only the first ": " separates the action; the message keeps its own colons.
        Assert.Equal(["Fix: the parser", "moving from main to feat", ""], log.Select(e => e.Message));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), log[0].Time);
        Assert.Equal("bbbb", log[1].Hash);
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
    public void TextDiff_can_ignore_whitespace_changes()
    {
        const string before = "if (x)\n{\n    run();\n}\n", after = "if (x)\n{\n\trun();  \n}\nnext();\n";
        var strict = TextDiff.Build(before, after);
        Assert.Equal((2, 1), (strict.Additions, strict.Deletions));

        var diff = TextDiff.Build(before, after, ignoreWhitespace: true);
        Assert.Equal((1, 0), (diff.Additions, diff.Deletions));
        // The re-indented line is unchanged, and each side shows its own text.
        var row = diff.AllRows[2];
        Assert.Equal(DiffLineKind.Context, row.Left!.Kind);
        Assert.Equal(("    run();", "    run();  "), (row.Left.Text, row.Right!.Text));
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
    public void Stats_parse_shortstat_and_group_people_by_email_and_week()
    {
        static string Head(string hash, string name, string email, DateTimeOffset when) =>
            $"\x1e{hash}\0{name}\0{email}\0{when.ToUnixTimeSeconds()}\n";
        var output = Head("a1", "Ana", "ana@x.com", new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero)) + "\n 2 files changed, 3 insertions(+), 1 deletion(-)\n"
                   + Head("b2", "Mei", "MEI@x.com", new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero)) + "\n 1 file changed, 10 insertions(+)\n"
                   + Head("c3", "Mei Lin", "mei@x.com", new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero)) + "\n 1 file changed, 1 insertion(+), 2 deletions(-)\n"
                   + Head("d4", "Ana", "ana@x.com", new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero)) + "\n 1 file changed\n";
        var commits = GitParsers.ParseStats(output, withLines: true);
        Assert.Equal(4, commits.Count);
        Assert.Equal(("a1", 3, 1), (commits[0].Hash, commits[0].Additions, commits[0].Deletions));
        Assert.Equal((0, 0), (commits[3].Additions, commits[3].Deletions)); // binary-only change: no lines
        Assert.Null(GitParsers.ParseStats(output, withLines: false)[0].Additions);

        var report = Stats.ContributorStats.Build(commits, since: null, now: new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(4, report.TotalCommits);
        var mei = report.Contributors.Single(c => c.Email.StartsWith("mei", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(("Mei Lin", 2, 11, 2), (mei.Name, mei.Commits, mei.Additions, mei.Deletions)); // same email, latest name
        Assert.Equal(report.Weeks.Count, mei.WeeklyCommits.Count);
        Assert.Equal(2, mei.WeeklyCommits.Sum());
        Assert.All(report.Weeks, w => Assert.Equal(DayOfWeek.Sunday, w.DayOfWeek));
    }

    [Fact]
    public void Status_entries_map_to_states()
    {
        var output = " M src/a.cs\0A  b.cs\0R  new.cs\0old.cs\0?? tmp/\0!! bin/\0UU c.cs\0 D gone.cs\0";
        var entries = GitParsers.ParseStatus(output);
        Assert.Equal(
            [("src/a.cs", FileStatusKind.Modified), ("b.cs", FileStatusKind.Added), ("new.cs", FileStatusKind.Renamed),
             ("tmp/", FileStatusKind.Untracked), ("bin/", FileStatusKind.Ignored), ("c.cs", FileStatusKind.Conflict),
             ("gone.cs", FileStatusKind.Deleted)],
            entries.Select(e => (e.Path, e.Kind)));
    }

    [Fact]
    public void Blocks_give_each_change_its_lines_on_both_sides()
    {
        // keep | old value → new value + extra | tail | gone (deleted) | end | last (added at the end)
        var diff = TextDiff.Build("keep\nold value\ntail\ngone\nend\n", "keep\nnew value\nextra\ntail\nend\nlast\n");

        Assert.Equal(
        [
            new DiffBlock(false, 0, 1, 0, 1),
            new DiffBlock(true, 1, 1, 1, 2),
            new DiffBlock(false, 2, 1, 3, 1),
            new DiffBlock(true, 3, 1, 4, 0),
            new DiffBlock(false, 4, 1, 4, 1),
            new DiffBlock(true, 5, 0, 5, 1),
        ], TextDiff.Blocks(diff.AllRows));
    }

    [Fact]
    public void TextDiff_reports_binary_content()
    {
        Assert.True(TextDiff.Build("a\0b", "a").IsBinary);
    }
}
