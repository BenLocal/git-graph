using GitGraph.App.ViewModels;

namespace GitGraph.App.Tests;

public sealed class OpenWithMenuTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gitgraph-openwith-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_dir, "open-with.toml");

    public OpenWithMenuTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task An_app_runs_its_command_on_the_quoted_path()
    {
        if (OperatingSystem.IsWindows()) return; // the command below is POSIX sh
        var target = Path.Combine(_dir, "a file's name.txt");
        var output = Path.Combine(_dir, "out.txt");
        File.WriteAllText(Config, $"""
            [[open]]
            name = "Echo"
            command = "printf %s ${"{"}path{"}"} > '{output}'"
            """);
        var host = new FakeHost { OpenWithConfigPath = Config };

        var items = OpenWithMenu.For(host, target);

        Assert.Equal(["Echo", "Edit open-with.toml…"], items.Select(i => i.Name));
        await items[0].Run.ExecuteAsync(null);
        Assert.Empty(host.Errors);
        Assert.Equal(target, File.ReadAllText(output));
    }

    [Fact]
    public async Task A_failing_command_is_reported()
    {
        File.WriteAllText(Config, "[[open]]\nname = \"Missing\"\ncommand = \"exit 3\"\n");
        var host = new FakeHost { OpenWithConfigPath = Config };

        await OpenWithMenu.For(host, _dir)[0].Run.ExecuteAsync(null);

        Assert.Contains("Could not open in Missing (exit code 3)", Assert.Single(host.Errors));
    }

    [Fact]
    public async Task A_broken_file_shows_an_item_that_reports_the_error()
    {
        File.WriteAllText(Config, "[[open]\n");
        var host = new FakeHost { OpenWithConfigPath = Config };

        var items = OpenWithMenu.For(host, _dir);

        Assert.Equal(["open-with.toml has an error…", "Edit open-with.toml…"], items.Select(i => i.Name));
        await items[0].Run.ExecuteAsync(null);
        Assert.StartsWith("open-with.toml:", Assert.Single(host.Errors));
    }
}
