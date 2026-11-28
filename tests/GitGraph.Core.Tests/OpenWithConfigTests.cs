using GitGraph.Core.Workspaces;

namespace GitGraph.Core.Tests;

public sealed class OpenWithConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gitgraph-openwith-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void A_missing_file_is_created_with_the_default_apps()
    {
        var path = Path.Combine(_dir, "open-with.toml");

        var (apps, error) = OpenWithConfig.Load(path);

        Assert.Null(error);
        Assert.True(File.Exists(path));
        Assert.Contains(apps, a => a.Name == "VS Code");
        Assert.Equal(apps, OpenWithConfig.Load(path).Apps);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_default_file_parses_on_every_platform(bool macOS)
    {
        var (apps, error) = OpenWithConfig.Parse(OpenWithConfig.DefaultToml(macOS));

        Assert.Null(error);
        Assert.Equal(macOS ? ["VS Code", "Winc"] : ["VS Code"], apps.Select(a => a.Name));
        Assert.All(apps, a => Assert.Contains("${path}", a.Command));
    }

    [Fact]
    public void Apps_keep_their_order_and_an_unnamed_app_is_named_after_its_command()
    {
        var (apps, error) = OpenWithConfig.Parse("""
            [[open]]
            name = "Zed"
            command = "zed ${path}"

            [[open]]
            command = "subl ${path}"
            """);

        Assert.Null(error);
        Assert.Equal([new OpenWithApp("Zed", "zed ${path}"), new OpenWithApp("subl ${path}", "subl ${path}")], apps);
    }

    [Theory]
    [InlineData("[[open]]\nname = \"Broken", null)]
    [InlineData("[[open]]\nname = \"No command\"", "needs a command")]
    [InlineData("open = \"code\"", "list of [[open]]")]
    public void A_bad_file_gives_an_error_and_no_apps(string toml, string? message)
    {
        var (apps, error) = OpenWithConfig.Parse(toml);

        Assert.Empty(apps);
        Assert.NotNull(error);
        if (message is not null) Assert.Contains(message, error);
    }

    [Fact]
    public void A_file_without_apps_is_empty_not_an_error()
    {
        Assert.Equal(([], null), OpenWithConfig.Parse("# nothing yet\n"));
    }
}
