using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;

namespace GitGraph.App.Services;

/// <summary>One way of drawing authors who have no picture; <see cref="Id"/> is what the settings keep.</summary>
public sealed record AvatarStyle(string Id, string Name);

/// <summary>
/// Author avatars, fetched the way GitLens does it: GitHub's avatar for GitHub noreply addresses, otherwise
/// Gravatar by the MD5 of the email. People without a Gravatar get a generated picture in the chosen
/// <see cref="Style"/>: one of Gravatar's own, or one from RoboHash or DiceBear (seeded with that MD5, never the
/// address). Images are cached on disk for a week. <see cref="Initials"/> and <see cref="Identicons"/> are drawn
/// here and send nothing.
/// </summary>
public static partial class AvatarService
{
    /// <summary>Pixels asked for: twice the largest avatar (40), so it stays sharp at 200 % scaling.</summary>
    public const int Size = 128;

    public const string Robots = "Robots";
    public const string Initials = "Initials";
    public const string Identicons = "Identicon";

    /// <summary>Gravatar's built-in pictures for people without one, in one request.</summary>
    public static readonly IReadOnlyList<AvatarStyle> GravatarStyles =
    [
        new("Gravatar/identicon", "Geometric patterns"),
        new("Gravatar/retro", "Pixel characters"),
        new("Gravatar/monsterid", "Monsters"),
        new("Gravatar/wavatar", "Cartoon faces"),
        new("Gravatar/mp", "Silhouettes"),
    ];

    /// <summary>RoboHash's other sets (Gravatar's robohash default is set 1, the robots).</summary>
    public static readonly IReadOnlyList<AvatarStyle> RoboHashStyles =
    [
        new("RoboHash/set2", "Monsters"),
        new("RoboHash/set3", "Robot heads"),
        new("RoboHash/set4", "Kittens"),
        new("RoboHash/set5", "Humans"),
    ];

    /// <summary>A choice of DiceBear's styles (dicebear.com/styles).</summary>
    public static readonly IReadOnlyList<AvatarStyle> DiceBearStyles =
    [
        new("DiceBear/identicon", "Identicon"),
        new("DiceBear/shapes", "Shapes"),
        new("DiceBear/rings", "Rings"),
        new("DiceBear/pixel-art", "Pixel art"),
        new("DiceBear/bottts", "Bottts"),
        new("DiceBear/thumbs", "Thumbs"),
        new("DiceBear/fun-emoji", "Fun emoji"),
        new("DiceBear/avataaars", "Avataaars"),
        new("DiceBear/lorelei", "Lorelei"),
        new("DiceBear/notionists", "Notionists"),
        new("DiceBear/adventurer", "Adventurer"),
        new("DiceBear/open-peeps", "Open Peeps"),
    ];

    private static string _style = Robots;

    /// <summary>The <see cref="AvatarStyle.Id"/> in use; an unknown one falls back to robots.</summary>
    public static string Style
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            // Pictures of the old style can go; the new style's come from its own cache files.
            Loaded.Clear();
            StyleChanged?.Invoke();
        }
    }

    /// <summary>Pictures are fetched: not initials or identicons, which are drawn here.</summary>
    public static bool UseImages => Style is not (Initials or Identicons);

    /// <summary>Raised when the avatar style changes, so avatars on screen redraw.</summary>
    public static event Action? StyleChanged;

    /// <summary>Also used by the update check, so both go through the same proxy.</summary>
    internal static readonly HttpClient Http = new(new HttpClientHandler { Proxy = ResolveProxy(), UseProxy = true })
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

    /// <summary>Waits for every avatar asked for so far (for the snapshot hook).</summary>
    public static Task WhenLoadedAsync() => Task.WhenAll(Loaded.Values);

    public static Task<Bitmap?> GetAsync(string email)
    {
        if (!UseImages) return Task.FromResult<Bitmap?>(null);
        var key = email.Trim().ToLowerInvariant();
        return Loaded.GetOrAdd(key, k => LoadAsync(k, Style));
    }

    private static async Task<Bitmap?> LoadAsync(string email, string style)
    {
        // The size is in the name, so smaller pictures cached before aren't used for a larger one.
        var file = Path.Combine(CacheDir, $"{Md5(email)}-{Size}-{style.Replace('/', '-')}.png");
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < MaxAge)
                return new Bitmap(file);

            await Gate.WaitAsync();
            try
            {
                var bytes = await FetchAsync(email, style);
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

    /// <summary>
    /// GitHub's picture for a GitHub address. Otherwise Gravatar: with one of its own defaults in one request, or
    /// asking it for a 404 when the person has no picture and then generating one elsewhere.
    /// </summary>
    private static async Task<byte[]> FetchAsync(string email, string style)
    {
        if (GitHubIdNoReply().Match(email) is { Success: true } id)
            return await Http.GetByteArrayAsync($"https://avatars.githubusercontent.com/u/{id.Groups[1].Value}?s={Size}&v=4");
        if (GitHubUserNoReply().Match(email) is { Success: true } user)
            return await Http.GetByteArrayAsync($"https://github.com/{user.Groups[1].Value}.png?size={Size}");
        var hash = Md5(email);
        var (source, name) = style.IndexOf('/') is var slash and > 0 ? (style[..slash], style[(slash + 1)..]) : (style, "");
        var fallback = source switch
        {
            "RoboHash" => $"https://robohash.org/{hash}.png?set={name}&size={Size}x{Size}",
            "DiceBear" => $"https://api.dicebear.com/9.x/{name}/png?seed={hash}&size={Size}",
            _ => null,
        };
        if (fallback is null)
            return await Http.GetByteArrayAsync($"https://www.gravatar.com/avatar/{hash}?s={Size}&d={(source == "Gravatar" ? name : "robohash")}");
        using var gravatar = await Http.GetAsync($"https://www.gravatar.com/avatar/{hash}?s={Size}&d=404");
        if (gravatar.StatusCode != HttpStatusCode.NotFound) return await gravatar.EnsureSuccessStatusCode().Content.ReadAsByteArrayAsync();
        return await Http.GetByteArrayAsync(fallback);
    }

    private static string Md5(string s) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(s)));
}
