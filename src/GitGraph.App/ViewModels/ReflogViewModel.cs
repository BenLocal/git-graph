using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>
/// Every recent move of HEAD: commits, checkouts, resets, merges, rebases. Commits a reset or rebase left behind are
/// still here, so a branch or a reset can bring them back.
/// </summary>
public sealed partial class ReflogViewModel : ObservableObject
{
    private readonly RepoTabViewModel _tab;
    private readonly GitRepository _repo;

    public ReflogViewModel(RepoTabViewModel tab, GitRepository repo)
    {
        _tab = tab;
        _repo = repo;
        _ = Loads.Track(LoadAsync());
    }

    public string Title => _tab.Title;
    public PendingLoads Loads { get; } = new();

    [ObservableProperty]
    private IReadOnlyList<ReflogRowViewModel> _entries = [];

    [ObservableProperty]
    private string? _message = "Loading…";

    private async Task LoadAsync()
    {
        try
        {
            var log = await _repo.GetReflogAsync("HEAD", 500);
            Entries = log.Select((e, i) => new ReflogRowViewModel(e, i, this)).ToList();
            Message = log.Count == 0 ? "Nothing has moved HEAD in this repository yet." : null;
        }
        catch (GitException e)
        {
            Message = $"Could not read the reflog: {e.Message}";
        }
    }

    // Each action reloads the list, since it moved HEAD itself.
    [RelayCommand]
    private async Task CreateBranch(ReflogRowViewModel row)
    {
        await _tab.CreateBranchAtAsync(row.Hash);
        await Loads.Track(LoadAsync());
    }

    [RelayCommand]
    private async Task Checkout(ReflogRowViewModel row)
    {
        await _tab.CheckoutHashAsync(row.Hash);
        await Loads.Track(LoadAsync());
    }

    [RelayCommand]
    private Task ResetSoft(ReflogRowViewModel row) => ResetAsync(row, ResetMode.Soft);

    [RelayCommand]
    private Task ResetMixed(ReflogRowViewModel row) => ResetAsync(row, ResetMode.Mixed);

    [RelayCommand]
    private Task ResetHard(ReflogRowViewModel row) => ResetAsync(row, ResetMode.Hard);

    private async Task ResetAsync(ReflogRowViewModel row, ResetMode mode)
    {
        await _tab.ResetToAsync(row.Hash, $"{row.ShortHash} ({row.Selector}, \"{row.Action}: {row.Message}\")", mode);
        await Loads.Track(LoadAsync());
    }

    [RelayCommand]
    private Task CopyHash(ReflogRowViewModel row) => _tab.Host.Dialogs.CopyTextAsync(row.Hash);
}

public sealed class ReflogRowViewModel(ReflogEntry entry, int index, ReflogViewModel owner)
{
    public ReflogViewModel Owner { get; } = owner;
    public string Hash { get; } = entry.Hash;
    public string ShortHash { get; } = entry.Hash[..7];
    /// <summary>git's name for the entry, HEAD@{n}, which commands take too.</summary>
    public string Selector { get; } = $"HEAD@{{{index}}}";
    public string Action { get; } = entry.Action;
    // "moving to 3f50d6d3bc72…": full hashes shortened, as everywhere else in the app.
    public string Message { get; } = System.Text.RegularExpressions.Regex.Replace(entry.Message, @"\b([0-9a-f]{7})[0-9a-f]{33}\b", "$1");
    public string When { get; } = Relative(entry.Time);
    public string TimeText { get; } = entry.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>"5 minutes ago", "3 days ago", then the date.</summary>
    internal static string Relative(DateTimeOffset time)
    {
        var age = DateTimeOffset.Now - time;
        static string Ago(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? Ago((int)age.TotalMinutes, "minute")
            : age.TotalDays < 1 ? Ago((int)age.TotalHours, "hour")
            : age.TotalDays < 30 ? Ago((int)age.TotalDays, "day")
            : time.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
