using System.Net;
using System.Text.Json;

namespace GitGraph.App.Services;

/// <summary>The outcome of asking GitHub for the latest release; <see cref="DownloadUrl"/> is set when there is a newer one.</summary>
public sealed record UpdateResult(string Message, string? DownloadUrl);

/// <summary>Help → About → Check for updates: compares this build with the latest release on GitHub. Runs only when asked.</summary>
public static class UpdateCheck
{
    public static async Task<UpdateResult> CheckAsync(CancellationToken ct = default)
    {
        // https://github.com/owner/repo → the releases API of owner/repo.
        var path = new Uri(AppInfo.RepositoryUrl).AbsolutePath.Trim('/');
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{path}/releases/latest");
        request.Headers.UserAgent.ParseAdd($"GitGraph/{AppInfo.Version}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        try
        {
            using var response = await AvatarService.Http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateResult("No releases found. If the repository is private, GitHub hides its releases from this check.", null);
            if (!response.IsSuccessStatusCode)
                return new UpdateResult($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}. Try again later.", null);

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = json.RootElement.GetProperty("html_url").GetString();
            var latest = tag.TrimStart('v');
            if (AppInfo.IsDevBuild) return new UpdateResult($"This is a development build. The latest release is {latest}.", url);
            return IsNewer(latest, AppInfo.Version)
                ? new UpdateResult($"Version {latest} is available.", url)
                : new UpdateResult($"You have the latest version ({AppInfo.Version}).", null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return new UpdateResult($"Could not reach GitHub: {e.Message}", null);
        }
    }

    /// <summary>Compares "1.2.3" style versions; a pre-release suffix ("-beta.1") is ignored.</summary>
    private static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate.Split('-')[0], out var c) && Version.TryParse(current.Split('-')[0], out var v) && c > v;
}
