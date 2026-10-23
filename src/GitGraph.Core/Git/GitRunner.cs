using System.Diagnostics;
using System.Text;

namespace GitGraph.Core.Git;

public sealed class GitException(string message, int exitCode, string args) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string Args { get; } = args;
}

public sealed record GitResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs the git CLI. Every call is a separate process; output is decoded as UTF-8. Disposing a Process doesn't close
/// streams read through StandardOutput/StandardError, so each call disposes them, or the pipes stay open until the GC.
/// </summary>
public static class GitRunner
{
    public static string GitPath { get; set; } = "git";

    public static async Task<GitResult> RunRawAsync(string workDir, IEnumerable<string> args, CancellationToken ct = default)
    {
        using var proc = Start(workDir, args);
        using var output = proc.StandardOutput;
        using var error = proc.StandardError;
        var stdout = output.ReadToEndAsync(ct);
        var stderr = error.ReadToEndAsync(ct);
        await WaitAsync(proc, ct);
        return new GitResult(proc.ExitCode, await stdout, await stderr);
    }

    /// <summary>Output as bytes, for file contents that aren't text (images).</summary>
    public static async Task<byte[]> RunBytesAsync(string workDir, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        using var proc = Start(workDir, args);
        using var output = proc.StandardOutput;
        using var error = proc.StandardError;
        using var buffer = new MemoryStream();
        var stdout = output.BaseStream.CopyToAsync(buffer, ct);
        var stderr = error.ReadToEndAsync(ct);
        await WaitAsync(proc, ct);
        await stdout;
        if (proc.ExitCode != 0) throw new GitException((await stderr).Trim(), proc.ExitCode, string.Join(' ', args));
        return buffer.ToArray();
    }

    private static async Task WaitAsync(Process proc, CancellationToken ct)
    {
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { /* already gone */ }
            throw;
        }
    }

    private static Process Start(string workDir, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(GitPath)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Read-only queries must never take the index lock away from the user's own git commands.
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("log.showSignature=false");
        foreach (var a in args) psi.ArgumentList.Add(a);

        var proc = Process.Start(psi) ?? throw new GitException("Could not start git.", -1, string.Join(' ', args));
        proc.StandardInput.Close();
        return proc;
    }

    public static async Task<string> RunAsync(string workDir, IEnumerable<string> args, CancellationToken ct = default, params int[] okExitCodes)
    {
        var argList = args as IReadOnlyList<string> ?? args.ToList();
        var r = await RunRawAsync(workDir, argList, ct);
        if (r.ExitCode != 0 && Array.IndexOf(okExitCodes, r.ExitCode) < 0)
        {
            var msg = string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr;
            throw new GitException(msg.Trim(), r.ExitCode, string.Join(' ', argList));
        }
        return r.StdOut;
    }
}
