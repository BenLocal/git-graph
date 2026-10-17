using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.ViewModels;

/// <summary>
/// A folder's files: a tree on the left, the selected file on the right. Which subfolders are open and
/// which file is selected are remembered per folder, so the tab reopens as it was left.
/// </summary>
public sealed partial class FolderTabViewModel : TabViewModel
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico"];

    private readonly IAppHost _host;

    internal bool CompactFolders => _host.CompactFolders;
    private readonly FolderViewState _state;
    private bool _loaded;
    private bool _restoring;
    private CancellationTokenSource? _fileCts;

    public FolderTabViewModel(string path, WorkspaceViewModel workspace, IAppHost host) : base(path, workspace)
    {
        _host = host;
        _state = host.FolderView(path);
        Root = new FileNodeViewModel(this, path, "", isDirectory: true);
        Subtitle = "folder";
    }

    public override bool IsFolder => true;
    public override string StateKey => "folder:" + Path;

    public FileNodeViewModel Root { get; }

    [ObservableProperty]
    private FileNodeViewModel? _selectedNode;

    // ---- The file on the right

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string? _fileInfo;

    /// <summary>The selected file's text, shown read-only in the code editor; null for no text file.</summary>
    [ObservableProperty]
    private string? _fileText;

    /// <summary>The selected file's name, which picks its syntax highlighting.</summary>
    public string? FileName => FilePath is null ? null : System.IO.Path.GetFileName(FilePath);

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private string? _fileMessage;

    public bool HasFile => FilePath != null;

    partial void OnFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(FileName));
    }

    public override void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Root.IsExpanded = true;
        RestoreState();
    }

    /// <summary>Reopens the remembered subfolders (parents first) and the selected file.</summary>
    private void RestoreState()
    {
        _restoring = true;
        try
        {
            foreach (var rel in _state.Expanded.OrderBy(p => p.Count(c => c == '/')))
                if (Find(rel) is { IsDirectory: true } node) node.IsExpanded = true;
            if (_state.Selected is { } selected && Find(selected) is { } file) SelectedNode = file;
        }
        finally { _restoring = false; }
    }

    /// <summary>
    /// Finds a node by relative path, reading folders on the way. A compact row covers several path
    /// segments, so each step takes the child whose path is the target or a folder above it.
    /// </summary>
    private FileNodeViewModel? Find(string relative)
    {
        var node = Root;
        while (node.RelativePath != relative)
        {
            node.EnsureChildren();
            var next = node.Children.FirstOrDefault(c => !c.IsPlaceholder &&
                (c.RelativePath == relative || relative.StartsWith(c.RelativePath + "/", StringComparison.Ordinal)));
            if (next is null) return null;
            node = next;
        }
        return node;
    }

    /// <summary>Re-reads the whole tree, after switching compact folders on or off, and reopens it as it was.</summary>
    public void Rebuild()
    {
        if (!_loaded) return;
        Root.Reset();
        Root.IsExpanded = true;
        RestoreState();
    }

    public double TreeWidth => _state.TreeWidth ?? 300;

    public void SaveTreeWidth(double width)
    {
        _state.TreeWidth = Math.Round(width);
        _host.SaveState();
    }

    internal void OnExpansionChanged(FileNodeViewModel node)
    {
        if (_restoring || node == Root) return;
        _state.Expanded.Remove(node.RelativePath);
        if (node.IsExpanded) _state.Expanded.Add(node.RelativePath);
        _host.SaveState();
    }

    partial void OnSelectedNodeChanged(FileNodeViewModel? oldValue, FileNodeViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
        var value = newValue;
        if (value is null || value.IsPlaceholder) return;
        if (!_restoring)
        {
            _state.Selected = value.RelativePath;
            _host.SaveState();
        }
        if (!value.IsDirectory) _ = LoadFileAsync(value);
    }

    private async Task LoadFileAsync(FileNodeViewModel node)
    {
        _fileCts?.Cancel();
        var cts = _fileCts = new CancellationTokenSource();
        FilePath = node.RelativePath;
        FileText = null;
        Image = null;
        FileMessage = null;
        try
        {
            var info = new FileInfo(node.FullPath);
            if (!info.Exists)
            {
                FileMessage = "This file no longer exists.";
                return;
            }
            FileInfo = FormatSize(info.Length);
            if (info.Length > MaxFileBytes)
            {
                FileMessage = $"This file is {FormatSize(info.Length)}, too large to show here. Open it in VS Code instead.";
                return;
            }
            if (ImageExtensions.Contains(System.IO.Path.GetExtension(node.Name).ToLowerInvariant()))
            {
                var bitmap = await Task.Run(() => new Bitmap(node.FullPath), cts.Token);
                if (cts.IsCancellationRequested) return;
                Image = bitmap;
                FileInfo = $"{FormatSize(info.Length)}, {bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}";
                return;
            }
            var bytes = await File.ReadAllBytesAsync(node.FullPath, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0))
            {
                FileMessage = "Binary file. There is no text to show.";
                return;
            }
            var text = await Task.Run(() => Decode(bytes), cts.Token);
            if (cts.IsCancellationRequested) return;
            var lineCount = text.Length == 0 ? 0 : text.Count(c => c == '\n') + (text.EndsWith('\n') ? 0 : 1);
            FileInfo = $"{FormatSize(info.Length)}, {lineCount} {(lineCount == 1 ? "line" : "lines")}";
            if (lineCount == 0) FileMessage = "This file is empty.";
            else FileText = text;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            FileMessage = $"Could not read this file: {e.Message}";
        }
    }

    private static string Decode(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };

    // ---- Actions

    /// <summary>Folds every subfolder; the next visit opens with all of them closed.</summary>
    [RelayCommand]
    private void CollapseAll()
    {
        _restoring = true;
        try
        {
            foreach (var n in Root.Descendants()) n.IsExpanded = false;
        }
        finally { _restoring = false; }
        _state.Expanded.Clear();
        _host.SaveState();
    }

    /// <summary>Picks up files added, removed or renamed outside the app, keeping open folders open.</summary>
    [RelayCommand]
    private void RefreshTree()
    {
        Root.Reload();
        if (SelectedNode is { IsDirectory: false } file) _ = LoadFileAsync(file);
    }

    public override void OnActivated() => RefreshTree();

    [RelayCommand]
    private void OpenInVsCode(FileNodeViewModel? node)
    {
        var target = node?.FullPath ?? Path;
        if (ExternalTools.OpenInVsCode(target) is { } error) _host.ReportError(error);
    }

    [RelayCommand]
    private Task CopyPath(FileNodeViewModel? node) => CopyAsync((node ?? SelectedNode)?.RelativePath, "Path copied");

    [RelayCommand]
    private Task CopyFullPath(FileNodeViewModel? node) => CopyAsync((node ?? SelectedNode)?.FullPath ?? Path, "Full path copied");

    [RelayCommand]
    private Task CopyContents() => CopyAsync(FileText, "Contents copied");

    [ObservableProperty]
    private string? _copiedNote;

    [ObservableProperty]
    private bool _isCopiedNoteVisible;

    private int _copyCount;

    private async Task CopyAsync(string? text, string note)
    {
        if (string.IsNullOrEmpty(text)) return;
        await _host.Dialogs.CopyTextAsync(text);
        CopiedNote = note;
        IsCopiedNoteVisible = true;
        var count = ++_copyCount;
        await Task.Delay(1600);
        if (count == _copyCount) IsCopiedNoteVisible = false;
    }

    public override void Dispose() => _fileCts?.Cancel();
}

/// <summary>A file or folder in a folder tab's tree. A folder lists its contents when first opened.</summary>
public sealed partial class FileNodeViewModel : ObservableObject
{
    private readonly FolderTabViewModel? _owner;
    private bool _childrenLoaded;

    /// <param name="name">The row's label; a compact row shows several folders, like "main/java/com/example".</param>
    public FileNodeViewModel(FolderTabViewModel owner, string fullPath, string relativePath, bool isDirectory, string? name = null)
    {
        _owner = owner;
        FullPath = fullPath;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        Name = name ?? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(fullPath));
        // A placeholder child gives an unopened folder its expander without reading the disk yet.
        if (isDirectory) Children.Add(Placeholder);
    }

    private FileNodeViewModel()
    {
        FullPath = RelativePath = Name = "";
        IsPlaceholder = true;
    }

    private static readonly FileNodeViewModel Placeholder = new();

    public string Name { get; }
    public string FullPath { get; }
    public string RelativePath { get; }
    public bool IsDirectory { get; }
    public bool IsPlaceholder { get; }
    public bool IsFile => !IsDirectory && !IsPlaceholder;
    public ObservableCollection<FileNodeViewModel> Children { get; } = [];
    public FolderTabViewModel? Owner => _owner;

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Bound to the tree row, and the only link between tree and selection: a remembered selection then
    /// shows even though it was set before the rows existed.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && _owner != null) _owner.SelectedNode = this;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) EnsureChildren();
        _owner?.OnExpansionChanged(this);
    }

    public void EnsureChildren()
    {
        if (!IsDirectory || _childrenLoaded) return;
        _childrenLoaded = true;
        Children.Clear();
        foreach (var child in ReadChildren()) Children.Add(child);
    }

    /// <summary>
    /// Re-reads loaded folders and updates the children in place: unchanged nodes stay, so open folders stay
    /// open and the selection survives (the tree drops the selection of any item that is removed, even briefly).
    /// </summary>
    public void Reload()
    {
        if (!IsDirectory || !_childrenLoaded) return;
        var existing = Children.Where(c => !c.IsPlaceholder).ToDictionary(c => (c.Name, c.IsDirectory));
        var target = ReadChildren().Select(f => existing.TryGetValue((f.Name, f.IsDirectory), out var keep) ? keep : f).ToList();

        for (var i = Children.Count - 1; i >= 0; i--)
            if (!target.Contains(Children[i])) Children.RemoveAt(i);
        for (var i = 0; i < target.Count; i++)
        {
            var at = Children.IndexOf(target[i]);
            if (at < 0) Children.Insert(i, target[i]);
            else if (at != i) Children.Move(at, i);
        }
        foreach (var c in target) c.Reload();
    }

    /// <summary>Forgets the loaded children, so the next open reads the folder again.</summary>
    public void Reset()
    {
        _childrenLoaded = false;
        IsExpanded = false;
        Children.Clear();
        if (IsDirectory) Children.Add(Placeholder);
    }

    public IEnumerable<FileNodeViewModel> Descendants()
    {
        foreach (var c in Children.Where(c => !c.IsPlaceholder))
        {
            yield return c;
            foreach (var d in c.Descendants()) yield return d;
        }
    }

    /// <summary>Folders first, then files, by name; .git is left out, as VS Code does.</summary>
    private List<FileNodeViewModel> ReadChildren()
    {
        var result = new List<FileNodeViewModel>();
        try
        {
            var dir = new DirectoryInfo(FullPath);
            var comparer = StringComparer.OrdinalIgnoreCase;
            foreach (var d in dir.EnumerateDirectories().Where(d => d.Name != ".git").OrderBy(d => d.Name, comparer))
            {
                // Compact folders: follow a chain of folders that each hold just one folder, as VS Code does.
                var (path, label) = (d, d.Name);
                while (_owner!.CompactFolders && OnlySubfolder(path) is { } only)
                    (path, label) = (only, $"{label}/{only.Name}");
                result.Add(new FileNodeViewModel(_owner!, path.FullName, Join(label), isDirectory: true, name: label));
            }
            foreach (var f in dir.EnumerateFiles().OrderBy(f => f.Name, comparer))
                result.Add(new FileNodeViewModel(_owner!, f.FullName, Join(f.Name), isDirectory: false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* unreadable: show it empty */ }
        return result;
    }

    /// <summary>The folder's single entry when that is a folder, or null (files, several entries, empty, unreadable).</summary>
    private static DirectoryInfo? OnlySubfolder(DirectoryInfo dir)
    {
        try
        {
            var entries = dir.EnumerateFileSystemInfos().Where(e => e.Name != ".git").Take(2).ToList();
            return entries is [DirectoryInfo only] ? only : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private string Join(string name) => RelativePath.Length == 0 ? name : $"{RelativePath}/{name}";
}
