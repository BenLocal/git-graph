using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Navigation;

namespace GitGraph.App.ViewModels;

public enum NavKind
{
    Definition,
    References,
    Implementations,
}

/// <summary>One result of a lookup, as the results list shows it.</summary>
/// <param name="FullPath">The file on disk.</param>
/// <param name="Location">Its path relative to the folder tab and the 1-based line, as "src/App.cs:12".</param>
public sealed record NavItem(string FullPath, int Offset, int Length, string Location, string LineText, string? Detail);

/// <summary>Where the editor should put the caret once it shows <paramref name="FilePath"/> (relative to the folder tab).</summary>
public sealed record RevealRequest(string FilePath, int Offset, int Length);

/// <summary>
/// Code navigation in the folder tab's viewer: go to definition, find references, go to implementation, the
/// occurrences of the name at the caret, and back / forward through the places jumped from. Each git work tree
/// (or the folder itself outside git) gets a <see cref="CodeIndex"/>, opened when a file of it is first shown and
/// synced in the background then and whenever the tab is activated.
/// </summary>
public sealed partial class FolderTabViewModel
{
    private readonly Dictionary<string, Task<CodeIndex?>> _indexes = new(PathComparer);
    private readonly Dictionary<string, (string Root, bool IsGit)> _navRoots = new(PathComparer);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _navCts;
    private readonly Stack<(string FullPath, int Offset)> _back = new();
    private readonly Stack<(string FullPath, int Offset)> _forward = new();

    /// <summary>The file shown, which may be outside the tree (a jump to a file above the folder).</summary>
    private FileNodeViewModel? _shownNode;

    private int _caretOffset;

    /// <summary>Set by a jump; the view moves the caret there once the file is on screen, then clears it.</summary>
    [ObservableProperty]
    private RevealRequest? _reveal;

    /// <summary>"Indexing 120 of 2,655 files…" while an index is built; null otherwise.</summary>
    [ObservableProperty]
    private string? _indexStatus;

    /// <summary>The results of the last lookup with more than one result, or none.</summary>
    public ObservableCollection<NavItem> NavResults { get; } = [];

    [ObservableProperty]
    private string? _navTitle;

    [ObservableProperty]
    private bool _isNavOpen;

    /// <summary>The result picked in the list, shown in the preview beside it; Enter or a double-click opens it.</summary>
    [ObservableProperty]
    private NavItem? _selectedNavItem;

    /// <summary>The text of the picked result's file, for the preview.</summary>
    [ObservableProperty]
    private string? _previewText;

    /// <summary>The preview's syntax colours by line number, after the text as in the viewer.</summary>
    [ObservableProperty]
    private IReadOnlyDictionary<int, SyntaxSpan[]>? _previewSyntax;

    /// <summary>The result the preview shows, set once <see cref="PreviewText"/> is its file's text.</summary>
    [ObservableProperty]
    private NavItem? _previewItem;

    private string? _previewPath;
    private CancellationTokenSource? _previewCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool _canGoForward;

    /// <summary>The work tree (or plain folder) a file belongs to for navigation, and its path there.</summary>
    private async Task<(string Root, bool IsGit, string Path)> NavRootAsync(FileNodeViewModel node, CancellationToken ct)
    {
        var dir = System.IO.Path.GetDirectoryName(node.FullPath)!;
        if (!_navRoots.TryGetValue(dir, out var root))
        {
            root = await GitRepository.FindTopLevelAsWrittenAsync(dir, ct) is { } top ? (top, true) : (Path, false);
            _navRoots[dir] = root;
        }
        return (root.Root, root.IsGit, System.IO.Path.GetRelativePath(root.Root, node.FullPath).Replace('\\', '/'));
    }

    private Task<CodeIndex?> IndexFor(string root, bool isGit)
    {
        if (!_indexes.TryGetValue(root, out var index)) _indexes[root] = index = OpenIndexAsync(root, isGit);
        return index;
    }

    private async Task<CodeIndex?> OpenIndexAsync(string root, bool isGit)
    {
        try
        {
            var index = await CodeIndex.OpenAsync(root, isGit, _lifetime.Token);
            index.ProgressChanged += UpdateIndexStatus;
            _ = Loads.Track(SyncAsync(index));
            return index;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception e)
        {
            // The native parser or SQLite failing to load: lookups are off for this folder, the viewer still works.
            _host.ReportError($"Code navigation is unavailable: {e.Message}");
            return null;
        }
    }

    private async Task SyncAsync(CodeIndex index)
    {
        try
        {
            await index.SyncAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Lookups fall back on what the index has; the next activation tries again.
            if (!_lifetime.IsCancellationRequested) _host.ReportError($"Could not index {index.Root}: {e.Message}");
        }
        UpdateIndexStatus();
    }

    /// <summary>Brings every index opened in this tab up to date, as files may have changed while it was hidden.</summary>
    private void SyncIndexes()
    {
        foreach (var task in _indexes.Values)
            if (task is { IsCompletedSuccessfully: true, Result: { } index }) _ = Loads.Track(SyncAsync(index));
    }

    private void UpdateIndexStatus()
    {
        var running = _indexes.Values.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result?.Progress).OfType<(int Done, int Total)>().ToList();
        IndexStatus = running.Count == 0 ? null : $"Indexing {running.Sum(p => p.Done):N0} of {running.Sum(p => p.Total):N0} files…";
    }

    /// <summary>A file was shown: its index opens (and syncs) if this is the first file of its work tree.</summary>
    private async Task OnFileShownAsync(FileNodeViewModel node)
    {
        _shownNode = node;
        if (NavLanguage.ForPath(node.Name) == null) return;
        try
        {
            var (root, isGit, _) = await NavRootAsync(node, _lifetime.Token);
            await IndexFor(root, isGit);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Looks up the name at <paramref name="offset"/> of the file shown.</summary>
    public async Task NavigateAsync(NavKind kind, int offset)
    {
        if (_shownNode is not { } node || FileText is not { } text) return;
        _navCts?.Cancel();
        var cts = _navCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            var (root, isGit, path) = await NavRootAsync(node, cts.Token);
            if (await IndexFor(root, isGit) is not { } index) return;
            var nav = index.Navigator;
            var result = kind switch
            {
                NavKind.Definition => await nav.DefinitionsAsync(path, text, offset, cts.Token),
                NavKind.References => await nav.ReferencesAsync(path, text, offset, cts.Token),
                _ => await nav.ImplementationsAsync(path, text, offset, cts.Token),
            };
            // Another file or lookup came first.
            if (cts.IsCancellationRequested || !ReferenceEquals(text, FileText)) return;
            if (result.Name is null) return;
            var items = result.Locations.Select(l => Item(root, l)).ToList();
            if (kind == NavKind.Definition && items.Count == 1)
            {
                Open(items[0]);
                return;
            }
            NavResults.Clear();
            foreach (var item in items) NavResults.Add(item);
            SelectedNavItem = items.FirstOrDefault();
            NavTitle = (kind, items.Count) switch
            {
                (NavKind.Definition, 0) => $"No definition found for {result.Name}",
                (NavKind.References, 0) => $"No references found for {result.Name}",
                (NavKind.Implementations, 0) => $"No implementations found for {result.Name}",
                (NavKind.Definition, var n) => $"{n} definitions of {result.Name}",
                (NavKind.References, 1) => $"1 reference to {result.Name}",
                (NavKind.References, var n) => $"{n:N0} references to {result.Name}",
                (_, 1) => $"1 implementation of {result.Name}",
                (_, var n) => $"{n} implementations of {result.Name}",
            };
            IsNavOpen = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (!cts.IsCancellationRequested) _host.ReportError($"Code navigation failed: {e.Message}");
        }
    }

    private NavItem Item(string root, NavLocation l)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, l.Path));
        var shown = System.IO.Path.GetRelativePath(Path, full).Replace('\\', '/');
        return new NavItem(full, l.Span.Start, l.Span.Length, $"{shown}:{l.Span.Line + 1}", l.LineText, l.Detail);
    }

    /// <summary>Where the name at the caret occurs in the file shown, for highlighting.</summary>
    public async Task<IReadOnlyList<TextSpan>> OccurrencesAsync(int offset, CancellationToken ct)
    {
        if (_shownNode is not { } node || FileText is not { } text || NavLanguage.ForPath(node.Name) == null) return [];
        var (root, isGit, path) = await NavRootAsync(node, ct);
        if (!_indexes.TryGetValue(root, out var opening) || await opening is not { } index) return [];
        var spans = await index.Navigator.OccurrencesAsync(path, text, offset, ct);
        return ReferenceEquals(text, FileText) ? spans : [];
    }

    /// <summary>The name a Ctrl+click at an offset would look up, for underlining it while Ctrl is held.</summary>
    public async Task<TextSpan?> NameAtAsync(int offset, CancellationToken ct)
    {
        if (_shownNode is not { } node || FileText is not { } text) return null;
        var (root, isGit, path) = await NavRootAsync(node, ct);
        if (await IndexFor(root, isGit) is not { } index) return null;
        var span = await index.Navigator.NameAtAsync(path, text, offset, ct);
        return ReferenceEquals(text, FileText) ? span : null;
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value != null) _ = Loads.Track(PreviewAsync(value));
    }

    partial void OnIsNavOpenChanged(bool value)
    {
        if (value) return;
        _previewCts?.Cancel();
        _previewPath = null;
        SelectedNavItem = null;
        PreviewItem = null;
        PreviewText = null;
        PreviewSyntax = null;
    }

    /// <summary>Shows a result's file in the preview, read once per file and coloured after it appears.</summary>
    private async Task PreviewAsync(NavItem item)
    {
        _previewCts?.Cancel();
        var cts = _previewCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            // The file again unless it is already shown and coloured (its colouring may have been cancelled midway).
            if (!PathComparer.Equals(_previewPath, item.FullPath) || PreviewSyntax is null)
            {
                var text = _shownNode != null && PathComparer.Equals(_shownNode.FullPath, item.FullPath) && FileText is { } shown
                    ? shown
                    : await Task.Run(() => Decode(File.ReadAllBytes(item.FullPath)), cts.Token);
                if (cts.IsCancellationRequested) return;
                _previewPath = item.FullPath;
                PreviewSyntax = null;
                PreviewText = text;
                PreviewItem = item;
                var lines = text.Split('\n').Select((line, i) => (i + 1, line.TrimEnd('\r')));
                var syntax = await Task.Run(() => SyntaxHighlighter.HighlightLines(item.FullPath, lines, cts.Token), cts.Token);
                if (!cts.IsCancellationRequested) PreviewSyntax = syntax;
            }
            else PreviewItem = item;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone since the lookup: nothing to preview, and opening it reports why.
            _previewPath = null;
            PreviewItem = null;
            PreviewText = null;
        }
    }

    /// <summary>Jumps to a result, remembering where it came from.</summary>
    [RelayCommand]
    private void Open(NavItem? item)
    {
        if (item is null) return;
        IsNavOpen = false;
        if (_shownNode is { } here)
        {
            _back.Push((here.FullPath, _caretOffset));
            _forward.Clear();
            UpdateHistory();
        }
        Show(item.FullPath, item.Offset, item.Length);
    }

    [RelayCommand]
    private void CloseNav() => IsNavOpen = false;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => Step(_back, _forward);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => Step(_forward, _back);

    private void Step(Stack<(string FullPath, int Offset)> from, Stack<(string FullPath, int Offset)> to)
    {
        if (from.Count == 0) return;
        if (_shownNode is { } here) to.Push((here.FullPath, _caretOffset));
        var (full, offset) = from.Pop();
        UpdateHistory();
        Show(full, offset, 0);
    }

    private void UpdateHistory()
    {
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
    }

    /// <summary>Shows a file (from the tree when it is in it) with the caret at an offset.</summary>
    private void Show(string fullPath, int offset, int length)
    {
        var relative = System.IO.Path.GetRelativePath(Path, fullPath).Replace('\\', '/');
        Reveal = new RevealRequest(relative, offset, length);
        if (_shownNode != null && PathComparer.Equals(_shownNode.FullPath, fullPath) && FileText != null) return;
        if (!relative.StartsWith("..", StringComparison.Ordinal) && Find(relative, expand: true) is { IsDirectory: false } node)
            SelectedNode = node;
        else
            _ = Loads.Track(LoadFileAsync(new FileNodeViewModel(this, fullPath, relative, isDirectory: false)));
    }

    private void DisposeNavigation()
    {
        _lifetime.Cancel();
        _navCts?.Cancel();
        foreach (var task in _indexes.Values)
            task.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result?.Dispose(); }, TaskScheduler.Default);
        _indexes.Clear();
    }
}
