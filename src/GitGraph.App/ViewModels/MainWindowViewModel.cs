using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.ViewModels;

/// <summary>What child view models need from the application shell.</summary>
public interface IAppHost
{
    IDialogService Dialogs { get; }
    bool ShowRemoteBranches { get; }
    bool SplitDiff { get; set; }
    bool ShowDetailsPanel { get; set; }
    void ReportError(string message);
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
        foreach (var w in _state.Workspaces) Workspaces.Add(new WorkspaceViewModel(w, this));
        _selectedWorkspace = Workspaces.FirstOrDefault(w => w.Config.Id == _state.ActiveWorkspaceId) ?? Workspaces[0];
        _selectedWorkspace.EnsureScanned();
        ApplyTheme();
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

    /// <summary>Hiding it only takes effect while a repository is open; the empty state needs the sidebar.</summary>
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

    [RelayCommand]
    private void TogglePanel() => ShowDetailsPanel = !ShowDetailsPanel;

    /// <summary>Maximizes or restores the commit details panel of the open tab, showing it first if hidden.</summary>
    [RelayCommand]
    private void ToggleMaximizePanel()
    {
        if (SelectedWorkspace.SelectedTab?.Details is not { } details) return;
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

    partial void OnSelectedWorkspaceChanged(WorkspaceViewModel value)
    {
        if (value is null) return;
        _state.ActiveWorkspaceId = value.Config.Id;
        value.EnsureScanned();
        SaveState();
    }

    public void ReportError(string message) => ErrorMessage = message;

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

    [RelayCommand]
    private void CycleTheme()
    {
        _state.Theme = _state.Theme switch { "System" => "Dark", "Dark" => "Light", _ => "System" };
        ApplyTheme();
        OnPropertyChanged(nameof(ThemeLabel));
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
