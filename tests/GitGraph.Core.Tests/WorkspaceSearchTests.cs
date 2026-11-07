using GitGraph.Core.Git;
using GitGraph.Core.Navigation;

namespace GitGraph.Core.Tests;

/// <summary>Quick open's file list and fuzzy ranking, and find in files, on a real temporary repository.</summary>
public sealed class WorkspaceSearchTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "gitgraph-search-" + Guid.NewGuid().ToString("N"));

    public WorkspaceSearchTests()
    {
        Directory.CreateDirectory(_repo);
        Git("init", "-q", "-b", "main");
        Write("src/App/Program.cs", "class Program\n{\n    // Find ME here\n}\n");
        Write("src/App/Readme.md", "﻿find me at the start\n");
        Write("docs/notes.txt", "naïve find me\r\nnothing\r\n");
        Write(".gitignore", "build/\n");
        Write("build/out.txt", "find me in an ignored file\n");
        Git("add", "src", ".gitignore");
        Git("commit", "-q", "-m", "files");
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, true); } catch { /* best effort */ }
    }

    private void Git(params string[] args) => GitRunner.RunAsync(_repo, args).GetAwaiter().GetResult();

    private void Write(string path, string content)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public async Task Files_are_tracked_and_untracked_ones_but_not_ignored()
    {
        var files = await WorkspaceSearch.ListFilesAsync(_repo);
        Assert.Equal([".gitignore", "docs/notes.txt", "src/App/Program.cs", "src/App/Readme.md"], files.Order(StringComparer.Ordinal));

        // A subfolder lists its own files, relative to it.
        Assert.Equal(["Program.cs", "Readme.md"], (await WorkspaceSearch.ListFilesAsync(Path.Combine(_repo, "src", "App"))).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_folder_git_ignores_is_walked()
    {
        Assert.Equal(["out.txt"], await WorkspaceSearch.ListFilesAsync(Path.Combine(_repo, "build")));
    }

    [Fact]
    public async Task A_folder_holding_repositories_searches_each_of_them()
    {
        // A workspace folder that isn't a repository: two repositories in it, one with a file it ignores.
        var folder = Path.Combine(Path.GetTempPath(), "gitgraph-search-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var name in new[] { "one", "two" })
            {
                var dir = Path.Combine(folder, name);
                Directory.CreateDirectory(dir);
                await GitRunner.RunAsync(dir, ["init", "-q"]);
                File.WriteAllText(Path.Combine(dir, $"{name}.txt"), $"find me in {name}\n");
            }
            File.WriteAllText(Path.Combine(folder, "two", ".gitignore"), "skip.txt\n");
            File.WriteAllText(Path.Combine(folder, "two", "skip.txt"), "find me, ignored\n");

            Assert.Equal(["one", "two"], (await WorkspaceSearch.RootsAsync(folder)).Select(r => r.Prefix));
            Assert.Equal(["one/one.txt", "two/.gitignore", "two/two.txt"], (await WorkspaceSearch.ListFilesAsync(folder)).Order(StringComparer.Ordinal));
            Assert.Equal(["one/one.txt", "two/two.txt"], (await WorkspaceSearch.SearchTextAsync(folder, "find me", 100)).Select(l => l.Path));
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task A_repository_inside_the_repository_is_not_listed_as_a_file()
    {
        // As in the sidebar, discovery doesn't look for repositories inside one; its untracked folder isn't a file either.
        var inner = Path.Combine(_repo, "vendor", "lib");
        Directory.CreateDirectory(inner);
        await GitRunner.RunAsync(inner, ["init", "-q"]);
        File.WriteAllText(Path.Combine(inner, "lib.txt"), "lib\n");

        Assert.Equal([""], (await WorkspaceSearch.RootsAsync(_repo)).Select(r => r.Prefix));
        Assert.DoesNotContain(await WorkspaceSearch.ListFilesAsync(_repo), f => f.StartsWith("vendor/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("prog", "src/App/Program.cs")]
    [InlineData("readme", "src/App/Readme.md")]
    [InlineData("app/rd", "src/App/Readme.md")]
    [InlineData("ntx", "docs/notes.txt")]
    public void A_file_name_match_beats_a_match_across_folders(string query, string best)
    {
        string[] files = ["src/App/Program.cs", "src/App/Readme.md", "docs/notes.txt", "src/Run/notify.ts"];
        var ranked = files.Select(f => (f, Score: FuzzyMatch.ScorePath(query, f))).Where(x => x.Score != null).OrderByDescending(x => x.Score);
        Assert.Equal(best, ranked.First().f);
    }

    [Fact]
    public void Fuzzy_scores_prefer_exact_then_prefix_then_word_starts()
    {
        Assert.Null(FuzzyMatch.Score("xyz", "Program"));
        Assert.Null(FuzzyMatch.Score("gp", "Program"));
        Assert.True(FuzzyMatch.Score("circle", "Circle") > FuzzyMatch.Score("circle", "CircleView"));
        Assert.True(FuzzyMatch.Score("circle", "CircleView") > FuzzyMatch.Score("circle", "BigCircle"));
        Assert.True(FuzzyMatch.Score("gv", "GraphView") > FuzzyMatch.Score("gv", "Gravity"));
    }

    [Fact]
    public async Task Text_search_finds_lines_with_offsets_the_viewer_can_use()
    {
        var found = await WorkspaceSearch.SearchTextAsync(_repo, "find me", 100);

        // Lower case ignores case; untracked files count, ignored ones don't.
        Assert.Equal(["docs/notes.txt:0", "src/App/Program.cs:2", "src/App/Readme.md:0"], found.Select(l => $"{l.Path}:{l.Span.Line}"));
        foreach (var l in found)
        {
            var text = File.ReadAllText(Path.Combine(_repo, l.Path));
            Assert.Equal("find me", text.Substring(l.Span.Start, l.Span.Length), ignoreCase: true);
        }
        Assert.Equal("// Find ME here", found[1].LineText);
        // After a multi-byte character, the column counts characters, not bytes.
        Assert.Equal("naïve ".Length, found[0].Span.Column);
    }

    [Fact]
    public async Task Text_search_with_a_capital_matches_case_and_stops_at_the_limit()
    {
        Assert.Equal(["src/App/Program.cs"], (await WorkspaceSearch.SearchTextAsync(_repo, "ME", 100)).Select(l => l.Path));
        Assert.Single(await WorkspaceSearch.SearchTextAsync(_repo, "find", 1));
        Assert.Empty(await WorkspaceSearch.SearchTextAsync(_repo, "no such text", 100));
    }

    [Fact]
    public async Task Text_search_outside_git_searches_the_folder()
    {
        var plain = Path.Combine(_repo, "build");
        Assert.Equal(["out.txt"], (await WorkspaceSearch.SearchTextAsync(plain, "ignored", 100)).Select(l => l.Path));
    }
}
