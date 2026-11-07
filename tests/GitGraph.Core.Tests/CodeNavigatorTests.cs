using GitGraph.Core.Git;
using GitGraph.Core.Navigation;

namespace GitGraph.Core.Tests;

/// <summary>
/// Definition, references and implementations across the files of a real temporary repository, once through the
/// index and once through the git grep fallback; both must give the same answers.
/// </summary>
public sealed class CodeNavigatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gitgraph-nav-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly string _indexFile;

    public CodeNavigatorTests()
    {
        _repo = Path.Combine(_root, "repo");
        _indexFile = Path.Combine(_root, "index.db");
        Directory.CreateDirectory(_repo);
        Git("init", "-q", "-b", "main");
        Write("src/Shapes/IShape.cs", """
            namespace Shapes;
            public interface IShape { double Area(); }
            """);
        Write("src/Shapes/Circle.cs", """
            namespace Shapes;
            public class Circle : IShape
            {
                public double Radius { get; set; }
                public double Area() => Radius * Radius;
            }
            public class BigCircle : Circle {}
            """);
        Write("src/Shapes/Square.cs", """
            namespace Shapes;
            public class Square : IShape
            {
                public double Area() => 4;
                public Square() {}
            }
            """);
        Write("src/App/Program.cs", """
            using Shapes;
            class Program
            {
                static double Run()
                {
                    var circle = new Circle { Radius = 2 };
                    IShape shape = circle;
                    var Radius = 3;
                    return shape.Area() + circle.Radius + Radius;
                }
            }
            """);
        Write("notes.md", "Circle and Square\nThe Circle again\n");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private void Git(params string[] args) => GitRunner.RunAsync(_repo, args).GetAwaiter().GetResult();

    private void Write(string path, string content)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private async Task<CodeNavigator> Navigator(bool useIndex)
    {
        if (!useIndex) return new CodeNavigator(_repo, true, null);
        using (var writer = SymbolIndex.Open(_indexFile))
            await IndexSync.SyncAsync(writer, _indexFile, _repo, true);
        return new CodeNavigator(_repo, true, SymbolIndex.Open(_indexFile));
    }

    /// <summary>Runs a lookup at the first occurrence of <paramref name="marker"/> in a file; results as path:line.</summary>
    private async Task<string[]> Lookup(bool useIndex, string path, string marker,
        Func<CodeNavigator, string, string, int, Task<NavResult>> lookup)
    {
        using var nav = await Navigator(useIndex);
        var text = File.ReadAllText(Path.Combine(_repo, path));
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, marker);
        var result = await lookup(nav, path, text, at);
        return result.Locations.Select(l => $"{l.Path}:{l.Span.Line + 1}").ToArray();
    }

    [Theory, InlineData(true), InlineData(false)]
    public async Task Definition_of_a_type_in_another_file(bool useIndex) =>
        Assert.Equal(["src/Shapes/Circle.cs:2"],
            await Lookup(useIndex, "src/App/Program.cs", "Circle {", (n, p, t, o) => n.DefinitionsAsync(p, t, o)));

    [Theory, InlineData(true), InlineData(false)]
    public async Task Definition_of_a_type_skips_its_constructors(bool useIndex) =>
        Assert.Equal(["src/Shapes/Square.cs:2"],
            await Lookup(useIndex, "src/Shapes/Square.cs", "Square : IShape", (n, p, t, o) => n.DefinitionsAsync(p, t, o)));

    [Theory, InlineData(true), InlineData(false)]
    public async Task Definition_of_a_local_stays_in_the_file(bool useIndex) =>
        Assert.Equal(["src/App/Program.cs:6"],
            await Lookup(useIndex, "src/App/Program.cs", "circle.Radius", (n, p, t, o) => n.DefinitionsAsync(p, t, o)));

    [Theory, InlineData(true), InlineData(false)]
    public async Task Definition_of_a_method_lists_every_declaration(bool useIndex) =>
        Assert.Equal(["src/Shapes/Circle.cs:5", "src/Shapes/IShape.cs:2", "src/Shapes/Square.cs:4"],
            await Lookup(useIndex, "src/App/Program.cs", "Area()", (n, p, t, o) => n.DefinitionsAsync(p, t, o)));

    [Theory, InlineData(true), InlineData(false)]
    public async Task References_skip_locals_with_the_same_name(bool useIndex) =>
        // Program.cs:8 and the last use on line 9 are the local `Radius`, not the property.
        Assert.Equal(["src/Shapes/Circle.cs:4", "src/Shapes/Circle.cs:5", "src/Shapes/Circle.cs:5", "src/App/Program.cs:6", "src/App/Program.cs:9"],
            await Lookup(useIndex, "src/Shapes/Circle.cs", "Radius {", (n, p, t, o) => n.ReferencesAsync(p, t, o)));

    [Theory, InlineData(true), InlineData(false)]
    public async Task References_of_a_local_are_its_uses(bool useIndex) =>
        Assert.Equal(["src/App/Program.cs:8", "src/App/Program.cs:9"],
            await Lookup(useIndex, "src/App/Program.cs", "Radius = 3", (n, p, t, o) => n.ReferencesAsync(p, t, o)));

    [Theory, InlineData(true), InlineData(false)]
    public async Task Implementations_of_an_interface_include_indirect_subtypes(bool useIndex) =>
        Assert.Equal(["src/Shapes/Circle.cs:2", "src/Shapes/Circle.cs:7", "src/Shapes/Square.cs:2"],
            await Lookup(useIndex, "src/Shapes/IShape.cs", "IShape {", (n, p, t, o) => n.ImplementationsAsync(p, t, o)));

    [Theory, InlineData(true), InlineData(false)]
    public async Task Implementations_of_an_interface_method_are_the_subtypes_methods(bool useIndex) =>
        Assert.Equal(["src/Shapes/Circle.cs:5", "src/Shapes/Square.cs:4"],
            await Lookup(useIndex, "src/Shapes/IShape.cs", "Area()", (n, p, t, o) => n.ImplementationsAsync(p, t, o)));

    [Fact]
    public async Task Files_without_a_grammar_match_whole_words()
    {
        var found = await Lookup(false, "notes.md", "Circle and", (n, p, t, o) => n.ReferencesAsync(p, t, o));
        Assert.Equal(["notes.md:1", "notes.md:2"], found[..2]);
        Assert.Contains("src/App/Program.cs:6", found);
    }

    [Fact]
    public async Task An_offset_off_any_name_finds_nothing()
    {
        var found = await Lookup(true, "src/App/Program.cs", "{\n", (n, p, t, o) => n.DefinitionsAsync(p, t, o));
        Assert.Empty(found);
    }

    [Fact]
    public async Task The_name_under_the_pointer_is_an_identifier_or_a_word()
    {
        using var nav = await Navigator(false);
        var path = "src/App/Program.cs";
        var text = File.ReadAllText(Path.Combine(_repo, path));
        var at = text.IndexOf("Circle {", StringComparison.Ordinal);
        Assert.Equal(new TextSpan(at, at + "Circle".Length, 5, at - text.LastIndexOf('\n', at) - 1),
            await nav.NameAtAsync(path, text, at + 2));
        Assert.Null(await nav.NameAtAsync(path, text, text.IndexOf("{\n", StringComparison.Ordinal)));
        // Without a grammar, the word, as Ctrl+click searches for it.
        Assert.Equal(new TextSpan(4, 10, 0, 4), await nav.NameAtAsync("notes.md", "The Circle again", 6));
    }

    [Fact]
    public async Task Occurrences_follow_scope_for_locals_and_name_for_the_rest()
    {
        using var nav = await Navigator(false);
        var path = "src/App/Program.cs";
        var text = File.ReadAllText(Path.Combine(_repo, path));
        var local = await nav.OccurrencesAsync(path, text, text.IndexOf("circle;", StringComparison.Ordinal));
        Assert.Equal([5, 6, 8], local.Select(u => u.Line));
        // The property `Radius`: the initializer and the member access, not the local of the same name.
        var property = await nav.OccurrencesAsync(path, text, text.IndexOf("Radius = 2", StringComparison.Ordinal));
        Assert.Equal([5, 8], property.Select(u => u.Line));
        Assert.Empty(await nav.OccurrencesAsync("notes.md", "Circle", 1));
    }

    [Fact]
    public async Task Symbol_search_ranks_the_closest_names_first()
    {
        using var nav = await Navigator(useIndex: true);

        var circles = await nav.SearchSymbolsAsync("circle", 10);
        Assert.Equal(["Circle", "BigCircle"], circles.Select(l => l.LineText));
        Assert.Equal("src/Shapes/Circle.cs", circles[0].Path);
        Assert.Equal(1, circles[0].Span.Line);
        Assert.Equal("class", circles[0].Detail);

        // Characters in order, not adjacent: "IShape" for "ish", "Area" in Circle for "ar".
        Assert.Equal("IShape", (await nav.SearchSymbolsAsync("ish", 10))[0].LineText);
        Assert.Contains(await nav.SearchSymbolsAsync("ar", 10), l => l.Detail == "method in Circle");
        Assert.Empty(await nav.SearchSymbolsAsync("zzz", 10));
        // LIKE's wildcards are plain characters in a query.
        Assert.Empty(await nav.SearchSymbolsAsync("%", 10));
    }
}
