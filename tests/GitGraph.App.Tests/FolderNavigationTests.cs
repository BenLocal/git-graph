using GitGraph.App.ViewModels;
using GitGraph.Core.Navigation;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.Tests;

/// <summary>Code navigation in a folder tab, against a real temporary repository and index.</summary>
public sealed class FolderNavigationTests : IDisposable
{
    private readonly TempRepo _repo = new();
    private readonly FakeHost _host = new();
    private readonly WorkspaceViewModel _workspace;
    private readonly FolderTabViewModel _folder;

    public FolderNavigationTests()
    {
        // Indexes of the test repositories stay out of the user's own index folder.
        SymbolIndex.Folder = Path.Combine(Path.GetTempPath(), "gitgraph-app-tests-index");
        Directory.CreateDirectory(Path.Combine(_repo.Path, "src"));
        _repo.Write("src/Shape.cs", "interface IShape { double Area(); }\n");
        _repo.Write("src/Circle.cs", "class Circle : IShape\n{\n    public double Area() => 3;\n}\n");
        _repo.Write("src/Square.cs", "class Square : IShape\n{\n    public double Area() => 4;\n}\n");
        _repo.Write("src/App.cs", "class App\n{\n    double Run(IShape shape) => shape.Area() + new Circle().Area();\n}\n");
        _repo.Git("add", ".");
        _repo.Git("commit", "-q", "-m", "shapes");
        _workspace = new WorkspaceViewModel(new WorkspaceConfig { Name = "Navigation test" }, _host);
        _folder = new FolderTabViewModel(_repo.Path, _workspace, _host);
    }

    public void Dispose()
    {
        _folder.Dispose();
        _workspace.Dispose();
        _repo.Dispose();
    }

    private async Task Idle() => await _folder.Loads.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

    private async Task<string> Show(string path)
    {
        _folder.SelectedNode = new FileNodeViewModel(_folder, Path.Combine(_repo.Path, path), path, isDirectory: false);
        await Idle();
        Assert.Equal(path, _folder.FilePath);
        return _folder.FileText!;
    }

    [Fact]
    public async Task Go_to_definition_with_one_result_opens_the_file_there()
    {
        var text = await Show("src/App.cs");

        await _folder.NavigateAsync(NavKind.Definition, text.IndexOf("Circle()", StringComparison.Ordinal));
        await Idle();

        Assert.Equal("src/Circle.cs", _folder.FilePath);
        Assert.Equal(new RevealRequest("src/Circle.cs", "class ".Length, "Circle".Length), _folder.Reveal);
        Assert.False(_folder.IsNavOpen);
        Assert.Empty(_host.Errors);
    }

    [Fact]
    public async Task Several_results_open_the_list_and_picking_one_jumps()
    {
        var text = await Show("src/App.cs");

        await _folder.NavigateAsync(NavKind.Implementations, text.IndexOf("IShape", StringComparison.Ordinal));

        Assert.True(_folder.IsNavOpen);
        Assert.Equal("2 implementations of IShape", _folder.NavTitle);
        Assert.Equal(["src/Circle.cs:1", "src/Square.cs:1"], _folder.NavResults.Select(r => r.Location));

        _folder.OpenCommand.Execute(_folder.NavResults[1]);
        await Idle();
        Assert.False(_folder.IsNavOpen);
        Assert.Equal("src/Square.cs", _folder.FilePath);
    }

    [Fact]
    public async Task Picking_a_result_previews_it_without_jumping()
    {
        var text = await Show("src/App.cs");
        await _folder.NavigateAsync(NavKind.Implementations, text.IndexOf("IShape", StringComparison.Ordinal));
        await Idle();

        // The first result is picked and previewed when the list opens.
        Assert.Equal(_folder.NavResults[0], _folder.PreviewItem);
        Assert.StartsWith("class Circle", _folder.PreviewText);

        _folder.SelectedNavItem = _folder.NavResults[1];
        await Idle();

        Assert.Equal(_folder.NavResults[1], _folder.PreviewItem);
        Assert.StartsWith("class Square", _folder.PreviewText);
        Assert.NotNull(_folder.PreviewSyntax);
        Assert.Equal("src/App.cs", _folder.FilePath);
        Assert.False(_folder.CanGoBack);

        _folder.CloseNavCommand.Execute(null);
        Assert.Null(_folder.PreviewText);
        Assert.Null(_folder.SelectedNavItem);
    }

    [Fact]
    public async Task The_name_under_the_pointer_is_found_for_the_underline()
    {
        var text = await Show("src/App.cs");
        var at = text.IndexOf("Circle()", StringComparison.Ordinal);

        var span = await _folder.NameAtAsync(at + 2, CancellationToken.None);

        Assert.NotNull(span);
        Assert.Equal("Circle", text[span.Value.Start..span.Value.End]);
        Assert.Null(await _folder.NameAtAsync(text.IndexOf("=>", StringComparison.Ordinal) + 1, CancellationToken.None));
    }

    [Fact]
    public async Task References_list_every_use_across_files()
    {
        var text = await Show("src/Shape.cs");

        await _folder.NavigateAsync(NavKind.References, text.IndexOf("Area", StringComparison.Ordinal));

        Assert.Equal("5 references to Area", _folder.NavTitle);
        Assert.Equal(["src/Shape.cs:1", "src/App.cs:3", "src/App.cs:3", "src/Circle.cs:3", "src/Square.cs:3"],
            _folder.NavResults.Select(r => r.Location));
    }

    [Fact]
    public async Task Back_and_forward_return_to_where_the_jump_started()
    {
        var text = await Show("src/App.cs");
        var from = text.IndexOf("Circle()", StringComparison.Ordinal);
        _folder.SetCaret(2, from);
        await _folder.NavigateAsync(NavKind.Definition, from);
        await Idle();
        Assert.True(_folder.CanGoBack);
        Assert.False(_folder.CanGoForward);

        _folder.GoBackCommand.Execute(null);
        await Idle();
        Assert.Equal("src/App.cs", _folder.FilePath);
        Assert.Equal(new RevealRequest("src/App.cs", from, 0), _folder.Reveal);
        Assert.True(_folder.CanGoForward);

        _folder.GoForwardCommand.Execute(null);
        await Idle();
        Assert.Equal("src/Circle.cs", _folder.FilePath);
    }

    [Fact]
    public async Task A_lookup_overtaken_by_another_file_does_nothing()
    {
        var text = await Show("src/App.cs");

        var lookup = _folder.NavigateAsync(NavKind.References, text.IndexOf("Area", StringComparison.Ordinal));
        _folder.SelectedNode = new FileNodeViewModel(_folder, Path.Combine(_repo.Path, "src/Square.cs"), "src/Square.cs", isDirectory: false);
        await lookup;
        await Idle();

        Assert.False(_folder.IsNavOpen);
        Assert.Empty(_folder.NavResults);
        Assert.Equal("src/Square.cs", _folder.FilePath);
    }

    [Fact]
    public async Task Occurrences_of_the_name_at_the_caret_are_found_in_the_file()
    {
        var text = await Show("src/App.cs");

        var spans = await _folder.OccurrencesAsync(text.IndexOf("shape.", StringComparison.Ordinal), CancellationToken.None);

        Assert.Equal(2, spans.Count);
        Assert.All(spans, s => Assert.Equal("shape", text[s.Start..s.End]));
    }
}
