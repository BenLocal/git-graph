using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GitGraph.App.ViewModels;

/// <summary>One tab's terminal. Hiding preserves the session; closing ends it.</summary>
public partial class TerminalViewModel(string path) : ObservableObject
{
    public string Path { get; } = path;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string? _status;

    public event Action? FocusRequested;
    public event Action? CloseRequested;

    [RelayCommand]
    private void Show()
    {
        IsVisible = true;
        FocusRequested?.Invoke();
    }

    [RelayCommand]
    private void Hide() => IsVisible = false;

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
        CloseRequested?.Invoke();
        Status = null;
    }
}
