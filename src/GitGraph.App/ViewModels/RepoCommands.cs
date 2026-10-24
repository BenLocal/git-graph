using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.ViewModels;

/// <summary>A custom command in a menu, ready to run on what the menu opened on.</summary>
public sealed record CustomCommandItem(string Name, IAsyncRelayCommand Run);

/// <summary>The user's custom commands for one repository or folder: those that apply to a commit, branch or file, and running them.</summary>
/// <param name="afterRun">What to do once a command is done, such as refreshing: it may have changed the repository.</param>
public sealed class RepoCommands(IAppHost host, string folder, Func<Task> afterRun)
{
    /// <param name="scope">"Commit", "Branch", "File" or "Repository".</param>
    /// <param name="values">The placeholders' values; asked for when a command runs.</param>
    public IReadOnlyList<CustomCommandItem> For(string scope, Func<Dictionary<string, string>> values) =>
        host.CustomCommands
            .Where(c => c.Scope == scope && !string.IsNullOrWhiteSpace(c.Command))
            .Select(c => new CustomCommandItem(Label(c), new AsyncRelayCommand(() => RunAsync(c, values()))))
            .ToList();

    private static string Label(CustomCommand c) => string.IsNullOrWhiteSpace(c.Name) ? c.Command.Trim() : c.Name.Trim();

    private async Task RunAsync(CustomCommand command, Dictionary<string, string> values)
    {
        values["repo"] = folder;
        var name = Label(command);
        if (command.Confirm && !await host.Dialogs.ConfirmAsync($"Run \"{name}\"",
                $"Run this in {folder}?\n\n{CustomCommandRunner.Expand(command.Command, values, OperatingSystem.IsWindows())}", "Run"))
            return;
        host.Notify($"Running {name}…");
        try
        {
            var (exitCode, output) = await CustomCommandRunner.RunAsync(command.Command, folder, values);
            // What it printed is worth seeing; a quiet success only needs the note.
            if (exitCode != 0) host.Dialogs.ShowOutput($"{name} failed (exit code {exitCode})", output.Length > 0 ? output : "It printed nothing.");
            else if (!string.IsNullOrWhiteSpace(output)) host.Dialogs.ShowOutput(name, output);
            host.Notify(exitCode == 0 ? $"{name} finished." : $"{name} failed with exit code {exitCode}.");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            host.ReportError($"Could not run {name}: {e.Message}");
        }
        await afterRun();
    }
}
