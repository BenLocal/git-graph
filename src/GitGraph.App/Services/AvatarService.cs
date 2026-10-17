using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;

namespace GitGraph.App.Services;

/// <summary>
/// Author avatars, fetched the way GitLens does it: GitHub's avatar for GitHub noreply addresses,
/// otherwise Gravatar by the MD5 of the email with the "robohash" default, so people without a
/// Gravatar still get a distinct robot. Images are cached on disk for a week.
/// </summary>
public static partial class AvatarService
{
    public const int Size = 64;

    private static bool _useImages = true;

    /// <summary>False shows initials and sends nothing to Gravatar or GitHub.</summary>
    public static bool UseImages
    {
        get => _useImages;
        set
        {
            if (_useImages == value) return;
            _useImages = value;
            StyleChanged?.Invoke();
        }
    }

    /// <summary>Raised when the avatar style changes, so avatars on screen redraw.</summary>
    public static event Action? StyleChanged;

    private static readonly HttpClient Http = new(new HttpClientHandler { Proxy = ResolveProxy(), UseProxy = true })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>
    /// The proxy from the environment, or else GNOME's manual system proxy: an app started from the
    /// desktop has no proxy variables, and .NET doesn't read the desktop's settings itself.
    /// </summary>
    private static IWebProxy? ResolveProxy()
    {
        foreach (var name in new[] { "HTTPS_PROXY", "https_proxy", "ALL_PROXY", "all_proxy" })
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))) return HttpClient.DefaultProxy;
        if (!OperatingSystem.IsLinux() || Gsettings("org.gnome.system.proxy", "mode") != "'manual'") return HttpClient.DefaultProxy;
        var host = Gsettings("org.gnome.system.proxy.https", "host")?.Trim('\'');
        var port = Gsettings("org.gnome.system.proxy.https", "port");
        return string.IsNullOrEmpty(host) || port is null or "0" ? HttpClient.DefaultProxy : new WebProxy($"http://{host}:{port}");
    }

    private static string? Gsettings(string schema, string key)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("gsettings", ["get", schema, key])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            return p.ExitCode == 0 ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    private static readonly SemaphoreSlim Gate = new(4);
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Loaded = new();
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitGraph", "avatars");

    [GeneratedRegex(@"^(\d+)\+[^@]+@users\.noreply\.github\.com$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubIdNoReply();

    [GeneratedRegex(@"^([^@+]+)@users\.noreply\.github\.com$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubUserNoReply();

    public static Task<Bitmap?> GetAsync(string email)
    {
        if (!UseImages) return Task.FromResult<Bitmap?>(null);
        var key = email.Trim().ToLowerInvariant();
        return Loaded.GetOrAdd(key, LoadAsync);
    }

    private static async Task<Bitmap?> LoadAsync(string email)
    {
        var file = Path.Combine(CacheDir, Md5(email) + ".png");
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < MaxAge)
                return new Bitmap(file);

            await Gate.WaitAsync();
            try
            {
                var bytes = await Http.GetByteArrayAsync(UrlFor(email));
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllBytesAsync(file, bytes);
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            finally { Gate.Release(); }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or ArgumentException)
        {
            // Offline or blocked: fall back to an expired copy if there is one, else initials.
            Loaded.TryRemove(email, out _);
            try { return File.Exists(file) ? new Bitmap(file) : null; }
            catch (Exception) { return null; }
        }
    }

    private static string UrlFor(string email)
    {
        if (GitHubIdNoReply().Match(email) is { Success: true } id)
            return $"https://avatars.githubusercontent.com/u/{id.Groups[1].Value}?s={Size}&v=4";
        if (GitHubUserNoReply().Match(email) is { Success: true } user)
            return $"https://github.com/{user.Groups[1].Value}.png?size={Size}";
        return $"https://www.gravatar.com/avatar/{Md5(email)}?s={Size}&d=robohash";
    }

    private static string Md5(string s) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(s)));
}
