using GitGraph.Core.Git;

namespace GitGraph.Core.Tests;

// Counts the process's open pipes, so no other test may start git at the same time.
[CollectionDefinition(nameof(GitRunnerTests), DisableParallelization = true)]
public sealed class GitRunnerCollection;

[Collection(nameof(GitRunnerTests))]
public sealed class GitRunnerTests
{
    private static int OpenPipes() =>
        new DirectoryInfo("/proc/self/fd").GetFileSystemInfos().Count(f => f.LinkTarget?.StartsWith("pipe:") == true);

    [Fact]
    public async Task Each_run_closes_its_pipes()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Path.GetTempPath();
        await GitRunner.RunAsync(dir, ["--version"]);
        var before = OpenPipes();

        for (var i = 0; i < 50; i++)
        {
            await GitRunner.RunAsync(dir, ["--version"]);
            await GitRunner.RunBytesAsync(dir, ["--version"]);
        }

        // Without closing the streams, every run left two read ends open until the GC finalized them.
        Assert.InRange(OpenPipes() - before, int.MinValue, 4);
    }
}
