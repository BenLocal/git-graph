using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GitGraph.App.Services;

/// <summary>
/// Runs the user's custom commands. ${name} placeholders are replaced by the values quoted for the shell, so a branch
/// name or commit subject can't break out of the command; the same values are in GITGRAPH_NAME environment variables.
/// </summary>
public static partial class CustomCommandRunner
{
    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex Placeholder();

    public static string Expand(string command, IReadOnlyDictionary<string, string> values, bool windows) =>
        Placeholder().Replace(command, m => values.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var v) ? Quote(v, windows) : m.Value);

    private static string Quote(string value, bool windows) =>
        windows ? "\"" + value.Replace("\"", "\"\"") + "\"" : "'" + value.Replace("'", "'\\''") + "'";

    /// <returns>The exit code, and what the command printed (output and errors as they came).</returns>
    public static async Task<(int ExitCode, string Output)> RunAsync(string command, string workDir, IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        var windows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo(windows ? "cmd.exe" : "/bin/sh")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(windows ? "/c" : "-c");
        psi.ArgumentList.Add(Expand(command, values, windows));
        foreach (var (key, value) in values) psi.Environment["GITGRAPH_" + key.ToUpperInvariant()] = value;

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        void Collect(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (output) output.Append(e.Data).Append('\n');
        }
        process.OutputDataReceived += Collect;
        process.ErrorDataReceived += Collect;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { /* already gone */ }
            throw;
        }
        lock (output) return (process.ExitCode, output.ToString());
    }
}
