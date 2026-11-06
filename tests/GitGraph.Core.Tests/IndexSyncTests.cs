using GitGraph.Core.Git;
using GitGraph.Core.Navigation;

namespace GitGraph.Core.Tests;

/// <summary>Syncs a symbol index against real temporary repositories.</summary>
public sealed class IndexSyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gitgraph-index-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly string _indexFile;
    private readonly SymbolIndex _index;

    public IndexSyncTests()
    {
        _repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_repo);
        _indexFile = Path.Combine(_root, "index.db");
        _index = SymbolIndex.Open(_indexFile);
        Git("init", "-q", "-b", "main");
        Git("config", "user.name", "Tester");
        Git("config", "user.email", "t@example.com");
        Git("config", "commit.gpgsign", "false");
        Write("src/Shape.cs", "interface IShape { double Area(); }\n");
        Write("src/Circle.cs", "class Circle : IShape { public double Area() => 1; }\n");
        Write("tools/build.py", "def build():\n    pass\n");
        Write("README.md", "# Shapes\n");
        Write(".gitignore", "generated/\n");
        Write("generated/Gen.cs", "class Generated {}\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "first");
    }

    public void Dispose()
    {
        _index.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private void Git(params string[] args) => GitRunner.RunAsync(_repo, args).GetAwaiter().GetResult();

    private void Write(string path, string content, string? root = null)
    {
        var full = Path.Combine(root ?? _repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private Task<SyncResult> Sync(string? root = null, bool isGit = true, CancellationToken ct = default) =>
        IndexSync.SyncAsync(_index, _indexFile, root ?? _repo, isGit, null, ct);

    private string[] Defined(string name, string? root = null) =>
        _index.FindDefinitions(root ?? _repo, name).Select(d => $"{d.Path}:{d.Symbol.Span.Line + 1}").ToArray();

    [Fact]
    public async Task First_sync_indexes_supported_tracked_and_untracked_files_only()
    {
        Write("src/Square.cs", "class Square : IShape { public double Area() => 2; }\n");

        var result = await Sync();

        Assert.Equal(4, result.Parsed);
        Assert.Equal(["src/Circle.cs:1", "src/Square.cs:1"], _index.FindDefinitions(_repo, "Area")
            .Where(d => d.Symbol.Container != "IShape").Select(d => $"{d.Path}:{d.Symbol.Span.Line + 1}"));
        Assert.Equal(["tools/build.py:1"], Defined("build"));
        Assert.Empty(Defined("Generated")); // ignored
        Assert.Equal(["src/Circle.cs", "src/Square.cs"], _index.FindSubtypes(_repo, "IShape").Select(s => s.Path));
        Assert.Equal(["src/Circle.cs", "src/Shape.cs", "src/Square.cs"], _index.FilesMentioning(_repo, "IShape"));
    }

    [Fact]
    public async Task Large_and_binary_files_are_not_parsed()
    {
        // MPEG transport stream videos share the .ts extension with TypeScript.
        var large = string.Concat(Enumerable.Repeat("export class Large {}\n", 60_000));
        Write("media/large.ts", large);
        File.WriteAllBytes(Path.Combine(_repo, "media/clip.ts"), [.. "class Video {}"u8, 0, 0x47]);
        Git("add", "media");
        Git("commit", "-q", "-m", "media");
        Write("media/untracked.ts", large.Replace("Large", "Untracked"));

        var result = await Sync();

        Assert.Equal(4, result.Parsed);
        Assert.Empty(Defined("Large"));
        Assert.Empty(Defined("Untracked"));
        Assert.Empty(Defined("Video"));
    }

    [Fact]
    public async Task A_second_sync_without_changes_parses_nothing()
    {
        await Sync();
        var again = await Sync();
        Assert.Equal(new SyncResult(0, 0, 0, 3), again);
    }

    [Fact]
    public async Task A_committed_change_reparses_only_that_file()
    {
        await Sync();
        Write("src/Circle.cs", "class Circle : IShape { public double Area() => 1; public double Radius; }\n");
        Git("commit", "-q", "-am", "radius");

        var result = await Sync();

        Assert.Equal(1, result.Parsed);
        Assert.Equal(["src/Circle.cs:1"], Defined("Radius"));
    }

    [Fact]
    public async Task Modified_unstaged_and_deleted_files_follow_the_work_tree()
    {
        await Sync();
        Write("src/Circle.cs", "class Round : IShape { public double Area() => 1; }\n");
        File.Delete(Path.Combine(_repo, "tools/build.py"));

        var result = await Sync();

        Assert.Equal(1, result.Parsed);
        Assert.Equal(1, result.Removed);
        Assert.Equal(["src/Circle.cs:1"], Defined("Round"));
        Assert.Empty(Defined("Circle"));
        Assert.Empty(Defined("build"));

        // Unchanged since the last sync: the stored hash is reused without reading the file.
        Assert.Equal(0, (await Sync()).Parsed);
    }

    [Fact]
    public async Task Switching_branches_reparses_only_the_changed_paths()
    {
        await Sync();
        Git("checkout", "-q", "-b", "feature");
        Write("src/Circle.cs", "class Circle : IShape { public double Area() => 3; }\n");
        Write("src/Triangle.cs", "class Triangle : IShape {}\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "feature");
        Git("checkout", "-q", "main");
        await Sync();

        Git("checkout", "-q", "feature");
        var toFeature = await Sync();
        Assert.Equal(2, toFeature.Parsed);
        Assert.Equal(["src/Triangle.cs:1"], Defined("Triangle"));

        Git("checkout", "-q", "main");
        var back = await Sync();
        Assert.Equal(1, back.Parsed);
        Assert.Equal(1, back.Removed);
        Assert.Empty(Defined("Triangle"));
    }

    [Fact]
    public async Task A_second_worktree_links_the_content_the_first_one_parsed()
    {
        await Sync();
        var other = Path.Combine(_root, "other");
        Git("worktree", "add", "-q", other, "-b", "other");

        var result = await Sync(other);

        Assert.Equal(0, result.Parsed);
        Assert.Equal(3, result.Linked);
        Assert.Equal(["src/Circle.cs:1"], Defined("Circle", other));
        Assert.Equal(["src/Circle.cs:1"], Defined("Circle"));
    }

    [Fact]
    public async Task A_plain_folder_is_walked_skipping_hidden_and_build_folders()
    {
        var folder = Path.Combine(_root, "plain");
        Write("main.go", "package main\nfunc Main() {}\n", folder);
        Write("pkg/util.go", "package pkg\nfunc Util() {}\n", folder);
        Write(".cache/x.go", "package x\nfunc Hidden() {}\n", folder);
        Write("node_modules/m/index.js", "function vendored() {}\n", folder);

        var result = await Sync(folder, isGit: false);

        Assert.Equal(2, result.Parsed);
        Assert.Equal(["pkg/util.go:2"], Defined("Util", folder));
        Assert.Empty(Defined("Hidden", folder));
        Assert.Empty(Defined("vendored", folder));

        Write("pkg/util.go", "package pkg\n\nfunc Util() {}\n", folder);
        Assert.Equal(1, (await Sync(folder, isGit: false)).Parsed);
        Assert.Equal(["pkg/util.go:3"], Defined("Util", folder));
    }

    [Fact]
    public async Task A_cancelled_sync_leaves_an_index_the_next_sync_completes()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sync(ct: cts.Token));

        var result = await Sync();
        Assert.Equal(3, result.Parsed);
        Assert.Equal(["src/Circle.cs:1"], Defined("Circle"));
    }

    [Fact]
    public async Task An_index_built_with_other_queries_is_rebuilt()
    {
        await Sync();
        _index.SetMeta("version", "0:old");
        using (var reopened = SymbolIndex.Open(_indexFile))
            Assert.Empty(reopened.FindDefinitions(_repo, "Circle"));
        Assert.Equal(3, (await Sync()).Parsed);
    }

    [Fact]
    public async Task Blob_hashes_match_git()
    {
        var content = "class Circle : IShape { public double Area() => 1; }\n"u8.ToArray();
        var git = (await GitRunner.RunAsync(_repo, ["hash-object", "src/Circle.cs"])).Trim();
        Assert.Equal(git, IndexSync.BlobHash(content));
    }
}
