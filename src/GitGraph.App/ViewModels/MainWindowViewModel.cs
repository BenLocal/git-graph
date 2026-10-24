using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Git;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.ViewModels;

/// <summary>What child view models need from the application shell.</summary>
public interface IAppHost
{
    IDialogService Dialogs { get; }
    bool ShowRemoteBranches { get; }
    bool SplitDiff { get; set; }
    bool ShowDetailsPanel { get; set; }
    /// <summary>The remembered state of a folder tab; changes are kept by <see cref="SaveState"/>.</summary>
    FolderViewState FolderView(string folder);
    bool CompactFolders { get; }
    PullMode PullMode { get; }
    bool IgnoreWhitespace { get; }
    bool WrapDiffLines { get; }
    void ReportError(string message);
    /// <summary>The user's own commands for the context menus.</summary>
    IReadOnlyList<CustomCommand> CustomCommands { get; }
    /// <summary>A note that isn't an error, such as new commits found by the periodic fetch.</summary>
    void Notify(string message);
    void SaveState();
}

public partial class MainWindowViewModel : ObservableObject, IAppHost
{
    private readonly WorkspaceStore _store;
    private readonly AppState _state;
    private IDialogService? _dialogs;

    public MainWindowViewModel(WorkspaceStore store)
    {
        _store = store;
        _state = store.Load();
        Services.AvatarService.UseImages = _state.AvatarStyle != "Initials";
        Services.ShortcutService.Instance.Load(_state.Keybindings);
        Services.ShortcutService.Instance.Changed += () =>
        {
            _state.Keybindings = Services.ShortcutService.Instance.Overrides.ToDictionary();
            SaveState();
        };
        foreach (var w in _state.Workspaces) Workspaces.Add(new WorkspaceViewModel(w, this));
        _selectedWorkspace = Workspaces.FirstOrDefault(w => w.Config.Id == _state.ActiveWorkspaceId) ?? Workspaces[0];
        _selectedWorkspace.IsCurrent = true;
        _selectedWorkspace.EnsureScanned();
        ApplyTheme();
        _state.FontScale = FontScale.Apply(_state.FontScale);
        StartAutoFetch();
    }

    public ObservableCollection<WorkspaceViewModel> Workspaces { get; } = [];

    [ObservableProperty]
    private WorkspaceViewModel _selectedWorkspace;

    [ObservableProperty]
    private string? _errorMessage;

    public IDialogService Dialogs
    {
        get => _dialogs ?? throw new InvalidOperationException("The window has not attached its dialog service yet.");
        set => _dialogs = value;
    }

    public bool ShowRemoteBranches => _state.ShowRemoteBranches;

    public bool SplitDiff
    {
        get => _state.SplitDiff;
        set
        {
            if (_state.SplitDiff == value) return;
            _state.SplitDiff = value;
            SaveState();
        }
    }

    public bool CompactFolders
    {
        get => _state.CompactFolders;
        set
        {
            if (_state.CompactFolders == value) return;
            _state.CompactFolders = value;
            OnPropertyChanged();
            SaveState();
            foreach (var tab in Workspaces.SelectMany(w => w.Tabs).OfType<FolderTabViewModel>()) tab.Rebuild();
        }
    }

    [RelayCommand]
    private void ToggleCompactFolders() => CompactFolders = !CompactFolders;

    public FolderViewState FolderView(string folder)
    {
        var key = System.IO.Path.TrimEndingDirectorySeparator(folder);
        if (!_state.FolderViews.TryGetValue(key, out var view)) _state.FolderViews[key] = view = new FolderViewState();
        return view;
    }

    public bool ShowDetailsPanel
    {
        get => _state.ShowDetailsPanel;
        set
        {
            if (_state.ShowDetailsPanel == value) return;
            _state.ShowDetailsPanel = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool ShowSidebar
    {
        get => _state.ShowSidebar;
        set
        {
            if (_state.ShowSidebar == value) return;
            _state.ShowSidebar = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    [RelayCommand]
    private void ToggleSidebar() => ShowSidebar = !ShowSidebar;

    /// <summary>Robots (GitHub pictures and Gravatar) or initials; switching redraws the avatars on screen.</summary>
    public bool AvatarRobots => _state.AvatarStyle != "Initials";
    public bool AvatarInitials => _state.AvatarStyle == "Initials";

    [RelayCommand]
    private void SetAvatarStyle(string style)
    {
        if (_state.AvatarStyle == style) return;
        _state.AvatarStyle = style;
        Services.AvatarService.UseImages = AvatarRobots;
        OnPropertyChanged(nameof(AvatarRobots));
        OnPropertyChanged(nameof(AvatarInitials));
        SaveState();
    }

    private DispatcherTimer? _autoFetch;

    private TimeSpan AutoFetchInterval => TimeSpan.FromMinutes(Math.Max(1, _state.AutoFetchMinutes));

    /// <summary>Minutes between automatic fetches; 0 when they are off.</summary>
    public int FetchEvery => _state.AutoFetch ? Math.Max(1, _state.AutoFetchMinutes) : 0;

    /// <param name="minutes">"0" turns automatic fetching off.</param>
    [RelayCommand]
    private void SetFetchInterval(string minutes)
    {
        var m = int.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture);
        _state.AutoFetch = m > 0;
        if (m > 0) _state.AutoFetchMinutes = m;
        if (_autoFetch != null) _autoFetch.Interval = AutoFetchInterval;
        OnPropertyChanged(nameof(FetchEvery));
        SaveState();
    }

    /// <summary>First fetch shortly after start, then every few minutes, for the workspace on screen.</summary>
    private void StartAutoFetch()
    {
        _autoFetch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autoFetch.Tick += async (_, _) =>
        {
            _autoFetch.Interval = AutoFetchInterval;
            if (_state.AutoFetch) await SelectedWorkspace.AutoFetchAsync();
        };
        _autoFetch.Start();
    }

    /// <summary>"Font size: 110%" in the View menu.</summary>
    public string FontSizeLabel => $"Font size: {FontScale.Current * 100:0}%";

    [RelayCommand]
    private void FontLarger() => SetFontScale(FontScale.Current + FontScale.Step);

    [RelayCommand]
    private void FontSmaller() => SetFontScale(FontScale.Current - FontScale.Step);

    [RelayCommand]
    private void FontReset() => SetFontScale(1);

    private void SetFontScale(double scale)
    {
        _state.FontScale = FontScale.Apply(scale);
        OnPropertyChanged(nameof(FontSizeLabel));
        SaveState();
    }

    public bool IgnoreWhitespace => _state.IgnoreWhitespace;
    public bool WrapDiffLines => _state.WrapDiffLines;

    [RelayCommand]
    private void ToggleIgnoreWhitespace()
    {
        _state.IgnoreWhitespace = !_state.IgnoreWhitespace;
        OnPropertyChanged(nameof(IgnoreWhitespace));
        DiffOptionsChanged();
    }

    [RelayCommand]
    private void ToggleWrapDiffLines()
    {
        _state.WrapDiffLines = !_state.WrapDiffLines;
        OnPropertyChanged(nameof(WrapDiffLines));
        DiffOptionsChanged();
    }

    private void DiffOptionsChanged()
    {
        foreach (var tab in Workspaces.SelectMany(w => w.Tabs).OfType<RepoTabViewModel>()) tab.Details.DiffOptionsChanged();
        SaveState();
    }

    public PullMode PullMode => Enum.TryParse<PullMode>(_state.PullMode, out var m) ? m : PullMode.GitConfig;

    [RelayCommand]
    private void SetPullMode(string mode)
    {
        _state.PullMode = mode;
        OnPropertyChanged(nameof(PullMode));
        SaveState();
    }

    [ObservableProperty]
    private string? _notice;

    public void Notify(string message) => Notice = message;

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    [RelayCommand]
    private void TogglePanel() => ShowDetailsPanel = !ShowDetailsPanel;

    /// <summary>Maximizes or restores the commit details panel of the open tab, showing it first if hidden.</summary>
    [RelayCommand]
    private void ToggleMaximizePanel()
    {
        if (SelectedWorkspace.SelectedRepoTab?.Details is not { } details) return;
        if (!ShowDetailsPanel)
        {
            ShowDetailsPanel = true;
            details.IsMaximized = true;
            return;
        }
        details.IsMaximized = !details.IsMaximized;
    }

    public string ThemeLabel => _state.Theme switch
    {
        "Dark" => "Night",
        "Light" => "Day",
        _ => "Follow system",
    };

    partial void OnSelectedWorkspaceChanged(WorkspaceViewModel? oldValue, WorkspaceViewModel newValue)
    {
        if (oldValue != null) oldValue.IsCurrent = false;
        if (newValue is null) return;
        newValue.IsCurrent = true;
        // Its open tab may have gone stale while another workspace was shown.
        newValue.SelectedTab?.EnsureLoaded();
    }

    partial void OnSelectedWorkspaceChanged(WorkspaceViewModel value)
    {
        if (value is null) return;
        _state.ActiveWorkspaceId = value.Config.Id;
        value.EnsureScanned();
        SaveState();
    }

    public void ReportError(string message) => ErrorMessage = message;

    public IReadOnlyList<CustomCommand> CustomCommands => _state.CustomCommands;

    [RelayCommand]
    private void EditCustomCommands() => Dialogs.ShowCustomCommands(new CustomCommandsViewModel(_state.CustomCommands, SaveState));

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    public void SaveState()
    {
        try { _store.Save(_state); }
        catch (IOException e) { ErrorMessage = $"Could not save workspaces to {_store.FilePath}: {e.Message}"; }
    }

    [RelayCommand]
    private async Task NewWorkspace()
    {
        var r = await Dialogs.PromptAsync("New workspace", "A workspace groups the folders whose repositories you want side by side.",
            [new PromptField("Name", "", "e.g. Client projects")], "Create workspace");
        if (r is null || string.IsNullOrWhiteSpace(r[0])) return;
        var config = new WorkspaceConfig { Name = r[0].Trim() };
        _state.Workspaces.Add(config);
        var vm = new WorkspaceViewModel(config, this);
        Workspaces.Add(vm);
        SelectedWorkspace = vm;
        SaveState();
    }

    [RelayCommand]
    private async Task RenameWorkspace()
    {
        var ws = SelectedWorkspace;
        var r = await Dialogs.PromptAsync("Rename workspace", null, [new PromptField("Name", ws.Name)], "Rename");
        if (r is null || string.IsNullOrWhiteSpace(r[0])) return;
        ws.Name = r[0].Trim();
        SaveState();
    }

    [RelayCommand]
    private async Task DeleteWorkspace()
    {
        var ws = SelectedWorkspace;
        if (Workspaces.Count == 1)
        {
            ErrorMessage = "You need at least one workspace. Create another one before deleting this one.";
            return;
        }
        if (!await Dialogs.ConfirmAsync("Delete workspace",
                $"Delete \"{ws.Name}\"? Its folders and repositories stay on disk; only the workspace is removed.",
                "Delete workspace", destructive: true))
            return;
        var index = Workspaces.IndexOf(ws);
        ws.Dispose();
        Workspaces.Remove(ws);
        _state.Workspaces.Remove(ws.Config);
        SelectedWorkspace = Workspaces[Math.Max(0, index - 1)];
        SaveState();
    }

    public bool ThemeSystem => _state.Theme is not ("Dark" or "Light");
    public bool ThemeDark => _state.Theme == "Dark";
    public bool ThemeLight => _state.Theme == "Light";

    [RelayCommand]
    private void CycleTheme() => SetTheme(_state.Theme switch { "System" => "Dark", "Dark" => "Light", _ => "System" });

    /// <param name="theme">System, Dark or Light.</param>
    [RelayCommand]
    private void SetTheme(string theme)
    {
        _state.Theme = theme;
        ApplyTheme();
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(ThemeSystem));
        OnPropertyChanged(nameof(ThemeDark));
        OnPropertyChanged(nameof(ThemeLight));
        SaveState();
    }

    private void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = _state.Theme switch
            {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };
    }
}
