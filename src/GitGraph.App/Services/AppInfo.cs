using System.Reflection;
using System.Runtime.InteropServices;

namespace GitGraph.App.Services;

/// <summary>What Help → About shows: the version (from the release tag), the commit it was built from, and the platform.</summary>
public static class AppInfo
{
    private static readonly Assembly Self = typeof(AppInfo).Assembly;

    private static string Informational =>
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev";

    /// <summary>"0.3.1" for a release, "0.0.0-dev" for a local build.</summary>
    public static string Version => Informational.Split('+')[0];

    /// <summary>The short commit hash the SDK recorded, or null when built outside git.</summary>
    public static string? Commit => Informational.Split('+') is [_, var sha, ..] ? sha[..Math.Min(7, sha.Length)] : null;

    public static bool IsDevBuild => Version.Contains("-dev", StringComparison.Ordinal);

    public static string RepositoryUrl =>
        Self.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value
        ?? "https://github.com/BenLocal/git-graph";

    public static string Runtime => RuntimeInformation.FrameworkDescription;

    public static string Avalonia => typeof(global::Avalonia.Application).Assembly.GetName().Version?.ToString(3) ?? "?";

    public static string OperatingSystem => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()})";

    /// <summary>Everything above as plain text, for bug reports.</summary>
    public static string Summary =>
        $"Git Graph {Version}{(Commit is { } c ? $" ({c})" : "")}\n.NET: {Runtime}\nAvalonia: {Avalonia}\nOS: {OperatingSystem}";
}
