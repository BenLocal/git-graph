using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Graph;

namespace GitGraph.App.ViewModels;

/// <summary>Filters on the commit list: author, period, path, code added or removed.</summary>
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

    /// <summary>Text whose introduction or removal to find, as written (spaces and case count).</summary>
    [ObservableProperty]
    private string _codeFilter = "";

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
        var filter = new CommitFilter(AuthorFilter.Trim(), since, PathFilter.Trim().Replace('\\', '/'), CodeFilter);
        return filter.IsEmpty ? null : filter;
    }

    // Typing waits for a pause before reloading; picking a period reloads at once.
    partial void OnAuthorFilterChanged(string value) => ApplyFiltersSoon();
    partial void OnPathFilterChanged(string value) => ApplyFiltersSoon();
    partial void OnCodeFilterChanged(string value) => ApplyFiltersSoon();
    partial void OnSinceFilterChanged(string value)
    {
        if (!_settingFilters) ApplyFilters();
    }

    partial void OnShowFiltersChanged(bool value)
    {
        if (value || !IsFiltered) return;
        _authorFilter = "";
        _pathFilter = "";
        _codeFilter = "";
        _sinceFilter = SinceOptions[0];
        OnPropertyChanged(nameof(AuthorFilter));
        OnPropertyChanged(nameof(PathFilter));
        OnPropertyChanged(nameof(CodeFilter));
        OnPropertyChanged(nameof(SinceFilter));
        ApplyFilters();
    }

    private void ApplyFiltersSoon()
    {
        if (_settingFilters) return;
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

    /// <summary>
    /// Enter in the find box: "author:… path:… code:… since:…" go into the filter fields (shown, so they can be seen and
    /// changed) and the rest stays to find; without any, it moves to the next match.
    /// </summary>
    [RelayCommand]
    private void FindEnter()
    {
        var query = FindQuery.Parse(FindText);
        if (query.Problem != null)
        {
            Host.ReportError(query.Problem);
            return;
        }
        if (!query.HasFilters)
        {
            FindNext();
            return;
        }
        // Set together, then reload once.
        _settingFilters = true;
        try
        {
            if (query.Author != null) AuthorFilter = query.Author;
            if (query.Path != null) PathFilter = query.Path;
            if (query.Code != null) CodeFilter = query.Code;
            if (query.Since != null) SinceFilter = query.Since;
            ShowFilters = true;
        }
        finally { _settingFilters = false; }
        FindText = query.Text;
        ApplyFilters();
    }

    private bool _settingFilters;
}
