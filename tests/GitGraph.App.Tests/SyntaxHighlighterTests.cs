using GitGraph.App.Services;
using GitGraph.App.ViewModels;
using GitGraph.Core.Diff;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.Tests;

public sealed class SyntaxHighlighterTests
{
    [Theory]
    [InlineData("Dockerfile", "FROM alpine:3.20")]
    [InlineData("containers/Dockerfile", "FROM alpine:3.20")]
    [InlineData("Dockerfile.dev", "FROM alpine:3.20")]
    [InlineData("Containerfile", "FROM alpine:3.20")]
    [InlineData("Containerfile.dev", "FROM alpine:3.20")]
    [InlineData("app.dockerfile", "FROM alpine:3.20")]
    [InlineData("Makefile", "TARGET := app")]
    [InlineData("build/Makefile", "TARGET := app")]
    [InlineData("makefile", "TARGET := app")]
    [InlineData("GNUmakefile", "TARGET := app")]
    [InlineData("OCamlMakefile", "TARGET := app")]
    [InlineData("Makefile.local", "TARGET := app")]
    [InlineData("build.mk", "TARGET := app")]
    [InlineData("build.mak", "TARGET := app")]
    public void Extensionless_build_files_have_syntax_colours(string path, string code)
    {
        var colours = SyntaxHighlighter.HighlightLines(path, [(1, code), (2, "# Build configuration")]);

        Assert.True(colours.TryGetValue(1, out var tokens), $"No syntax colours for {path}");
        Assert.NotEmpty(tokens!);
        Assert.True(colours.TryGetValue(2, out var comment), $"No comment colours for {path}");
        Assert.Contains(tokens!, token => token.Dark != comment![0].Dark && token.Light != comment[0].Light);
    }

    [Fact]
    public void Diff_cache_keeps_extensionless_languages_separate()
    {
        const string text = "FROM alpine\nTARGET := app\n";
        var diff = TextDiff.Build("", text);
        var expected = SyntaxHighlighter.HighlightLines("Makefile", [(1, "FROM alpine"), (2, "TARGET := app")]);

        SyntaxHighlighter.Highlight("Dockerfile", diff);
        var actual = SyntaxHighlighter.Highlight("Makefile", diff);

        Assert.True(expected.TryGetValue(2, out var spans));
        Assert.Equal(spans, actual.Line(old: false, 2));
    }

    [Fact]
    public void Build_filename_does_not_assign_its_grammar_to_other_files_with_the_same_suffix()
    {
        SyntaxHighlighter.HighlightLines("Dockerfile.dev", [(1, "FROM alpine")]);
        Assert.Empty(SyntaxHighlighter.HighlightLines("notes.dev", [(1, "FROM alpine")]));
        Assert.Empty(SyntaxHighlighter.HighlightLines("README", [(1, "FROM alpine")]));
    }

    [Theory]
    [InlineData("Dockerfile", "FROM alpine:3.20\n# Build image\n")]
    [InlineData("Makefile", "TARGET := app\n# Build project\n")]
    public async Task Opening_build_files_populates_the_editors_syntax(string name, string text)
    {
        using var repo = new TempRepo();
        repo.Write(name, text);
        var host = new FakeHost();
        using var workspace = new WorkspaceViewModel(new WorkspaceConfig { Name = "Syntax test" }, host);
        using var folder = new FolderTabViewModel(repo.Path, workspace, host);

        folder.SelectedNode = new FileNodeViewModel(folder, Path.Combine(repo.Path, name), name, isDirectory: false);
        await folder.Loads.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(text, folder.FileText);
        Assert.NotNull(folder.FileSyntax);
        Assert.NotEmpty(folder.FileSyntax[1]);
        Assert.NotEmpty(folder.FileSyntax[2]);
        Assert.Empty(host.Errors);
    }
}
