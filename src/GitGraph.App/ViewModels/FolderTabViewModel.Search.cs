using CommunityToolkit.Mvvm.ComponentModel;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Navigation;

namespace GitGraph.App.ViewModels;

public enum SearchKind
{
    /// <summary>Go to file (Ctrl+P): file paths, fuzzily.</summary>
    Files,

    /// <summary>Go to symbol in workspace (Ctrl+T): definitions in the symbol index, fuzzily.</summary>
    Symbols,

    /// <summary>Find in files (Ctrl+Shift+F): lines containing the text.</summary>
    Text,
}

/// <summary>
/// Searches typed into the results list: go to file, go to symbol and find in files. They share the list and preview
/// of the lookups; the list gets a query box, and picking a result jumps there as a lookup's result does.
/// </summary>
public sealed partial class FolderTabViewModel
{
    /// <summary>The most results a search lists.</summary>
    public const int MaxSearchResults = 500;

    // Typing waits this long for the next key before searching; find in files starts a git process each time.
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);

    private CancellationTokenSource? _searchCts;

    // The folder's files and where its searches run, read once per search opened.
    private Task<List<string>>? _files;
    private Task<List<SearchRoot>>? _roots;

    /// <summary>The search the list is showing, or null for a lookup's results (or no list).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(SearchPlaceholder))]
    private SearchKind? _searchKind;

    [ObservableProperty]
    private string _searchQuery = "";

    public bool IsSearching => SearchKind != null;

    public string SearchPlaceholder => SearchKind switch
    {
        ViewModels.SearchKind.Files => "Go to file by name",
        ViewModels.SearchKind.Symbols => "Go to a class, function or other definition by name",
        _ => "Find in files",
    };

    /// <summary>Opens the list with an empty query box for a search.</summary>
    public void StartSearch(SearchKind kind)
    {
        _navCts?.Cancel();
        _files = null;
        _roots = null;
        SearchKind = kind;
        if (SearchQuery.Length == 0) _ = Loads.Track(SearchAsync());
        else SearchQuery = "";
        IsNavOpen = true;
    }

    partial void OnSearchQueryChanged(string value) => _ = Loads.Track(SearchAsync());

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        if (SearchKind is not { } kind) return;
        var cts = _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var query = SearchQuery.Trim();
        try
        {
            if (query.Length == 0)
            {
                ShowSearchResults([], kind switch
                {
                    ViewModels.SearchKind.Files => "Type part of a file name or path",
                    ViewModels.SearchKind.Symbols => "Type part of a name",
                    _ => "Type the text to find",
                });
                return;
            }
            await Task.Delay(SearchDelay, cts.Token);
            var (items, noun) = kind switch
            {
                ViewModels.SearchKind.Files => (await SearchFilesAsync(query, cts.Token), "file"),
                ViewModels.SearchKind.Symbols => (await SearchSymbolsAsync(query, cts.Token), "definition"),
                _ => ((await WorkspaceSearch.SearchTextAsync(Path, query, MaxSearchResults, cts.Token)).Select(l => Item(Path, l)).ToList(), "match"),
            };
            if (cts.IsCancellationRequested) return;
            ShowSearchResults(items, items.Count switch
            {
                0 => $"No {Plural(noun)} found",
                1 => $"1 {noun}",
                MaxSearchResults => $"First {MaxSearchResults} {Plural(noun)}",
                var n => $"{n:N0} {Plural(noun)}",
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (!cts.IsCancellationRequested) _host.ReportError($"Search failed: {e.Message}");
        }
    }

    private static string Plural(string noun) => noun == "match" ? "matches" : noun + "s";

    private void ShowSearchResults(List<NavItem> items, string title)
    {
        NavResults.Clear();
        foreach (var item in items) NavResults.Add(item);
        SelectedNavItem = items.FirstOrDefault();
        NavTitle = title;
    }

    private async Task<List<NavItem>> SearchFilesAsync(string query, CancellationToken ct)
    {
        var files = await (_files ??= WorkspaceSearch.ListFilesAsync(Path, _lifetime.Token));
        return await Task.Run(() => files
            .Select(f => (Path: f, Score: FuzzyMatch.ScorePath(query, f)))
            .Where(x => x.Score != null)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .Take(MaxSearchResults)
            .Select(x => new NavItem(System.IO.Path.Combine(Path, x.Path), 0, 0, x.Path, "", null))
            .ToList(), ct);
    }

    /// <summary>
    /// Definitions in the folder: from the index of its own work tree (only the folder's part of it) and of each
    /// repository inside it, or of the folder itself outside git; best first across all of them.
    /// </summary>
    private async Task<List<NavItem>> SearchSymbolsAsync(string query, CancellationToken ct)
    {
        var found = new List<(NavLocation Location, NavItem Item)>();
        foreach (var root in await (_roots ??= WorkspaceSearch.RootsAsync(Path, _lifetime.Token)))
        {
            var indexRoot = root.IsGit && await GitRepository.FindTopLevelAsWrittenAsync(root.Dir, ct) is { } top ? top : root.Dir;
            if (await IndexFor(indexRoot, root.IsGit) is not { } index) continue;
            var under = System.IO.Path.GetRelativePath(indexRoot, root.Dir).Replace('\\', '/') is var r && r != "." ? r + "/" : "";
            foreach (var l in await index.Navigator.SearchSymbolsAsync(query, MaxSearchResults, under, ct))
                found.Add((l, Item(indexRoot, l)));
        }
        return found
            .OrderByDescending(x => FuzzyMatch.Score(query, x.Location.LineText))
            .ThenBy(x => x.Location.LineText.Length)
            .ThenBy(x => x.Item.Location, StringComparer.Ordinal)
            .Take(MaxSearchResults)
            .Select(x => x.Item)
            .ToList();
    }
}
