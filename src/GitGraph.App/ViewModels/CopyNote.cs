using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GitGraph.App.ViewModels;

/// <summary>The small note or check mark that confirms a copy, then goes away on its own.</summary>
public sealed partial class CopyNote : ObservableObject
{
    /// <summary>How long a confirmation stays; long enough to read a two-word note.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(1.6);

    [ObservableProperty]
    private string? _text;

    [ObservableProperty]
    private bool _isVisible;

    // Made on first use, so view models built off the UI thread (in tests) don't need a dispatcher.
    private DispatcherTimer? _timer;

    public void Show(string? text = null)
    {
        Text = text;
        IsVisible = true;
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = Duration };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                IsVisible = false;
            };
        }
        // Another copy while it shows starts the time again.
        _timer.Stop();
        _timer.Start();
    }
}
