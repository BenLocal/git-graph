using GitGraph.App.Services;
using GitGraph.App.ViewModels;
using GitGraph.Core.Git;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.Tests;

/// <summary>Stands in for the main window: settings the tests can change, dialogs that answer at once, errors recorded.</summary>
internal sealed class FakeHost : IAppHost, IDialogService
{
    public List<string> Errors { get; } = [];
    public List<string> Confirmations { get; } = [];
    public bool ConfirmAnswer { get; set; } = true;
    public bool IgnoreWhitespace { get; set; }

    public IDialogService Dialogs => this;
    public bool ShowRemoteBranches => true;
    public bool SplitDiff { get; set; }
    public bool ShowDetailsPanel { get; set; } = true;
    public FolderViewState FolderView(string folder) => new();
    public bool CompactFolders => true;
    public PullMode PullMode => PullMode.GitConfig;
    public bool WrapDiffLines => true;
    public void ReportError(string message) => Errors.Add(message);
    public void Notify(string message) { }
    public void SaveState() { }

    public Task<string[]?> PromptAsync(string title, string? message, IReadOnlyList<PromptField> fields, string confirmText) =>
        Task.FromResult<string[]?>(fields.Select(f => f.Value).ToArray());

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false)
    {
        Confirmations.Add(title);
        return Task.FromResult(ConfirmAnswer);
    }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task CopyTextAsync(string text) => Task.CompletedTask;
    public void ShowDiff(FullDiffViewModel diff) { }
    public void ShowFileHistory(FileHistoryViewModel history) { }
}

/// <summary>A throwaway repository with one commit of a.txt and b.txt.</summary>
internal sealed class TempRepo : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gitgraph-app-tests-" + Guid.NewGuid().ToString("N"));

    public TempRepo()
    {
        Directory.CreateDirectory(Path);
        Git("init", "-q", "-b", "main");
        Git("config", "user.name", "Tester");
        Git("config", "user.email", "t@example.com");
        Git("config", "commit.gpgsign", "false");
        Write("a.txt", "one\ntwo\n");
        Write("b.txt", "bee\n");
        Git("add", ".");
        Git("commit", "-q", "-m", "first");
    }

    public void Write(string file, string text) => File.WriteAllText(System.IO.Path.Combine(Path, file), text);

    public string Git(params string[] args) => GitRunner.RunAsync(Path, args).GetAwaiter().GetResult();

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { /* best effort */ }
    }
}

internal static class Wait
{
    /// <summary>View models load in the background; wait for the state a test expects (or fail after a while).</summary>
    public static async Task Until(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Timed out waiting for {what}.");
    }
}
