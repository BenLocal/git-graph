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
    public void UnifiedDiff_numbers_lines_and_pairs_split_rows()
    {
        var diff = """
            diff --git a/f b/f
            --- a/f
            +++ b/f
            @@ -1,3 +1,3 @@
             keep
            -old
            +new
            +extra
             tail
            """;
        var parsed = UnifiedDiff.Parse(diff);

        Assert.Equal(1, parsed.Deletions);
        Assert.Equal(2, parsed.Additions);
        var added = parsed.Lines.Where(l => l.Kind == DiffLineKind.Added).ToList();
        Assert.Equal([2, 3], added.Select(l => l.NewLine!.Value));
        Assert.Equal(3, parsed.Lines.Single(l => l.Text == "tail").OldLine);

        var split = UnifiedDiff.ToSplit(parsed.Lines);
        // hunk, keep, old|new, (empty)|extra, tail
        Assert.Equal(5, split.Count);
        Assert.Equal("old", split[2].Left!.Text);
        Assert.Equal("new", split[2].Right!.Text);
        Assert.Null(split[3].Left);
    }
}
