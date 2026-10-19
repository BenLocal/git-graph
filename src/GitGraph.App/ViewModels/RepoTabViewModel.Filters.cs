using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Graph;

namespace GitGraph.App.ViewModels;

/// <summary>Filters on the commit list: author, period, path.</summary>
public partial class RepoTabViewModel
{
    // ---- Filters -----------------------------------------------------------------------------

    public static IReadOnlyList<string> SinceOptions { get; } = ["Any time", "Last week", "Last month", "Last 3 months", "Last year"];

    /// <summary>The row of filter fields under the toolbar; hiding it clears the filters.</summary>
    [ObservableProperty]
    private bool _showFilters;

    [ObservableProperty]
    private string _authorFilter = "";

    [ObservableProperty]
    private string _pathFilter = "";

    [ObservableProperty]
    private string _sinceFilter = SinceOptions[0];

    private DispatcherTimer? _filterDebounce;

    public bool IsFiltered => CurrentFilter() != null;

    private CommitFilter? CurrentFilter()
    {
        DateTimeOffset? since = SinceFilter switch
        {
            "Last week" => DateTimeOffset.Now.AddDays(-7),
            "Last month" => DateTimeOffset.Now.AddMonths(-1),
            "Last 3 months" => DateTimeOffset.Now.AddMonths(-3),
            "Last year" => DateTimeOffset.Now.AddYears(-1),
            _ => null,
        };
        var filter = new CommitFilter(AuthorFilter.Trim(), since, PathFilter.Trim().Replace('\\', '/'));
        return filter.IsEmpty ? null : filter;
    }

    // Typing waits for a pause before reloading; picking a period reloads at once.
    partial void OnAuthorFilterChanged(string value) => ApplyFiltersSoon();
    partial void OnPathFilterChanged(string value) => ApplyFiltersSoon();
    partial void OnSinceFilterChanged(string value) => ApplyFilters();

    partial void OnShowFiltersChanged(bool value)
    {
        if (value || !IsFiltered) return;
        _authorFilter = "";
        _pathFilter = "";
        _sinceFilter = SinceOptions[0];
        OnPropertyChanged(nameof(AuthorFilter));
        OnPropertyChanged(nameof(PathFilter));
        OnPropertyChanged(nameof(SinceFilter));
        ApplyFilters();
    }

    private void ApplyFiltersSoon()
    {
        if (_filterDebounce is null)
        {
            _filterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _filterDebounce.Tick += (_, _) =>
            {
                _filterDebounce.Stop();
                ApplyFilters();
            };
        }
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }

    private void ApplyFilters()
    {
        _filterDebounce?.Stop();
        _maxCount = PageSize;
        OnPropertyChanged(nameof(IsFiltered));
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void ToggleFilters() => ShowFilters = !ShowFilters;
}
