using GitGraph.Core.Workspaces;

namespace GitGraph.Core.Tests;

public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gitgraph-state-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void Custom_commands_are_saved_and_read_back()
    {
        var store = new WorkspaceStore(_file);
        var state = store.Load();
        state.CustomCommands.Add(new CustomCommand { Name = "Run tests", Scope = "Commit", Command = "make test REV=${sha}", Confirm = true });
        store.Save(state);

        var command = Assert.Single(new WorkspaceStore(_file).Load().CustomCommands);
        Assert.Equal(("Run tests", "Commit", "make test REV=${sha}", true), (command.Name, command.Scope, command.Command, command.Confirm));
    }
}
