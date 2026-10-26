using Porta.Pty;

namespace GitGraph.App.Services;

/// <summary>Owns a PTY independently of its view, including cancellation during startup.</summary>
public sealed class TerminalSession : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private IPtyConnection? _connection;
    private bool _disposed;
    private bool _started;

    public async Task<IPtyConnection> StartAsync(string path, int columns, int rows)
    {
        CancellationToken cancellation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) throw new InvalidOperationException("The terminal session has already been started.");
            _started = true;
            cancellation = _stop.Token;
        }
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Terminal folder not found: {path}");
        var shell = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")
            : Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrEmpty(shell)) shell = OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/sh";
        var connection = await PtyProvider.SpawnAsync(new PtyOptions
        {
            Name = "Git Graph",
            App = shell,
            CommandLine = OperatingSystem.IsWindows() ? ["-NoLogo"] : ["-l"],
            Cwd = path,
            Cols = columns,
            Rows = rows,
            Environment = new Dictionary<string, string>
            {
                ["TERM"] = "xterm-256color",
                ["COLORTERM"] = "truecolor",
            },
        }, cancellation);
        lock (_gate)
        {
            if (_disposed)
            {
                connection.Dispose();
                throw new OperationCanceledException();
            }
            _connection = connection;
        }
        return connection;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            _stop.Dispose();
            // Porta.Pty closes the PTY and terminates the child on disposal.
            _connection?.Dispose();
            _connection = null;
        }
    }
}
