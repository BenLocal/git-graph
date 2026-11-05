using GitGraph.Core.Git;
using GitGraph.Core.Navigation;

namespace GitGraph.App.Services;

/// <summary>
/// Code navigation for one git work tree or plain folder: its symbol index, kept in sync in the background, and a
/// navigator reading it. The sync writes through one connection and the navigator reads through another, since a
/// SQLite connection can't be shared between threads.
/// </summary>
public sealed class CodeIndex : IDisposable
{
    private readonly SymbolIndex _writer;
    private readonly SymbolIndex _reader;
    private readonly CancellationTokenSource _cts = new();
    private Task? _sync;
    private bool _syncAgain;

    private CodeIndex(string root, bool isGit, string file, SymbolIndex writer, SymbolIndex reader)
    {
        Root = root;
        IsGit = isGit;
        File = file;
        _writer = writer;
        _reader = reader;
        Navigator = new CodeNavigator(root, isGit, reader);
    }

    public string Root { get; }
    public bool IsGit { get; }
    private string File { get; }
    public CodeNavigator Navigator { get; }

    /// <summary>Files parsed so far and in all while a sync parses; null otherwise.</summary>
    public (int Done, int Total)? Progress { get; private set; }

    /// <summary>Raised on the thread that started the sync (the UI thread in the app) when <see cref="Progress"/> changes.</summary>
    public event Action? ProgressChanged;

    /// <summary>Opens the index of a work tree (shared by the repository's worktrees) or plain folder.</summary>
    public static async Task<CodeIndex> OpenAsync(string root, bool isGit, CancellationToken ct)
    {
        var key = isGit && await new GitRepository(root).GetCommonDirAsync(ct) is { } common ? common : root;
        var file = SymbolIndex.PathFor(key);
        return await Task.Run(() => new CodeIndex(root, isGit, file, SymbolIndex.Open(file), SymbolIndex.Open(file)), ct);
    }

    /// <summary>
    /// Brings the index up to date in the background. A request during a sync runs one more sync after it, so
    /// changes made meanwhile are picked up; the returned task ends with the last one.
    /// </summary>
    public Task SyncAsync()
    {
        if (_sync is { IsCompleted: false })
        {
            _syncAgain = true;
            return _sync;
        }
        return _sync = RunSyncsAsync();
    }

    private async Task RunSyncsAsync()
    {
        // Progress<T> reports through the synchronization context it was made on, so on the UI thread here.
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            Progress = p.Done < p.Total ? p : null;
            ProgressChanged?.Invoke();
        });
        do
        {
            _syncAgain = false;
            await IndexSync.SyncAsync(_writer, File, Root, IsGit, progress, _cts.Token);
        } while (_syncAgain && !_cts.IsCancellationRequested);
    }

    public void Dispose()
    {
        _cts.Cancel();
        // The sync stops at its next check; close the connections after it, not under it.
        var sync = _sync ?? Task.CompletedTask;
        sync.ContinueWith(_ =>
        {
            Navigator.Dispose();
            _reader.Dispose();
            _writer.Dispose();
            _cts.Dispose();
        }, TaskScheduler.Default);
    }
}
