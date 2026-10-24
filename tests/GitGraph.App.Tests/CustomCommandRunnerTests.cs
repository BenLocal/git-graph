using GitGraph.App.Services;

namespace GitGraph.App.Tests;

/// <summary>Custom commands: placeholders are filled in quoted, so values can't break out of the command.</summary>
public sealed class CustomCommandRunnerTests
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["sha"] = "abc123",
        ["subject"] = "Fix \"it\"; it's $HOME's fault `rm -rf /`",
        ["file"] = "src/my file.txt",
    };

    [Fact]
    public void Placeholders_are_quoted_for_the_shell()
    {
        Assert.Equal("echo 'abc123' 'src/my file.txt'", CustomCommandRunner.Expand("echo ${sha} ${file}", Values, windows: false));
        Assert.Equal("""echo 'Fix "it"; it'\''s $HOME'\''s fault `rm -rf /`'""", CustomCommandRunner.Expand("echo ${subject}", Values, windows: false));
        Assert.Equal("echo \"abc123\" \"src/my file.txt\"", CustomCommandRunner.Expand("echo ${SHA} ${file}", Values, windows: true));
        // A placeholder with no value here is left as it is, so the mistake shows.
        Assert.Equal("echo ${branch}", CustomCommandRunner.Expand("echo ${branch}", Values, windows: false));
    }

    [Fact]
    public async Task A_command_runs_in_the_folder_with_the_values_in_its_environment()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var ok = await CustomCommandRunner.RunAsync("pwd; echo ${subject}; echo \"$GITGRAPH_SHA\"", dir, Values);
            Assert.Equal(0, ok.ExitCode);
            Assert.Equal([Path.GetFullPath(dir), Values["subject"], "abc123"], ok.Output.TrimEnd().Split('\n'));

            var failed = await CustomCommandRunner.RunAsync("echo oops >&2; exit 3", dir, Values);
            Assert.Equal((3, "oops"), (failed.ExitCode, failed.Output.Trim()));
        }
        finally { Directory.Delete(dir, true); }
    }
}
