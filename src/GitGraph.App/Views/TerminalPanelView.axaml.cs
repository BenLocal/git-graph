using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;
using Iciclecreek.Terminal;

namespace GitGraph.App.Views;

/// <summary>Hosts one terminal per open tab; the controls remain attached while hidden.</summary>
public partial class TerminalPanelView : UserControl
{
    private sealed record Entry(TerminalControl Control, TerminalSession Session, Action Close)
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly Dictionary<TerminalViewModel, Entry> _entries = [];
    private TerminalViewModel? _current;

    public TerminalPanelView()
    {
        InitializeComponent();
        IsVisible = false;
    }

    /// <summary>Used by the snapshot hook to wait for the visible PTY without a fixed delay.</summary>
    public Task WhenStartedAsync() => _current != null && _entries.TryGetValue(_current, out var entry)
        ? entry.Started.Task : Task.CompletedTask;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_current != null)
        {
            _current.PropertyChanged -= OnCurrentChanged;
            _current.FocusRequested -= FocusTerminal;
        }
        _current = DataContext as TerminalViewModel;
        if (_current != null)
        {
            _current.PropertyChanged += OnCurrentChanged;
            _current.FocusRequested += FocusTerminal;
        }
        ShowCurrent();
    }

    private void OnCurrentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalViewModel.IsVisible)) ShowCurrent();
    }

    private void ShowCurrent()
    {
        IsVisible = _current?.IsVisible == true;
        foreach (var (vm, entry) in _entries) entry.Control.IsVisible = vm == _current && IsVisible;
        if (!IsVisible || _current == null) return;
        if (!_entries.ContainsKey(_current)) CreateTerminal(_current);
        FocusTerminal();
    }

    private void CreateTerminal(TerminalViewModel vm)
    {
        var control = new TerminalControl
        {
            // The session owns startup and shutdown; the control only renders and forwards input.
            Process = "",
            BufferSize = 5000,
            CursorBlink = Motion.Enabled,
            AllowWindowOps = false,
            Options = new XTerm.Options.TerminalOptions { MinimumContrastRatio = 4.5 },
        };
        control[!FontFamilyProperty] = this.GetResourceObservable("CodeFont").ToBinding();
        control[!FontSizeProperty] = this.GetResourceObservable("FontSizeCode").ToBinding();
        control[!ForegroundProperty] = this.GetResourceObservable("TextBrush").ToBinding();
        control[!BackgroundProperty] = this.GetResourceObservable("SurfaceBrush").ToBinding();
        control[!TerminalControl.CursorColorProperty] = this.GetResourceObservable("TextColor").ToBinding();
        control[!TerminalControl.SelectionBrushProperty] = this.GetResourceObservable("SignalWashBrush").ToBinding();
        var entry = new Entry(control, new TerminalSession(), () => RemoveTerminal(vm));
        _entries.Add(vm, entry);
        vm.CloseRequested += entry.Close;
        vm.Status = "Starting…";
        control.ProcessExited += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            if (_entries.TryGetValue(vm, out var current) && current == entry)
                vm.Status = $"Shell exited ({e.ExitCode})";
        });
        control.Loaded += Start;
        Sessions.Children.Add(control);

        async void Start(object? sender, RoutedEventArgs e)
        {
            control.Loaded -= Start;
            try
            {
                var connection = await entry.Session.StartAsync(vm.Path, control.Terminal.Cols, control.Terminal.Rows);
                if (!_entries.TryGetValue(vm, out var current) || current != entry) return;
                // Layout can change while spawning. AttachConnection does not synchronise the
                // PTY size, and an unchanged layout will not trigger the library's resize path.
                connection.Resize(control.Terminal.Cols, control.Terminal.Rows);
                control.AttachConnection(connection);
                vm.Status = null;
                if (_current == vm && vm.IsVisible) FocusTerminal();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (_entries.TryGetValue(vm, out var current) && current == entry)
                    vm.Status = $"Unable to start shell: {ex.Message}";
            }
            finally { entry.Started.TrySetResult(); }
        }
    }

    private void FocusTerminal()
    {
        var vm = _current;
        Dispatcher.UIThread.Post(() =>
        {
            if (vm != null && _current == vm && IsVisible && _entries.TryGetValue(vm, out var entry))
                entry.Control.Focus();
        }, DispatcherPriority.Input);
    }

    private void RemoveTerminal(TerminalViewModel vm)
    {
        if (!_entries.Remove(vm, out var entry)) return;
        vm.CloseRequested -= entry.Close;
        entry.Control.Dispose();
        entry.Session.Dispose();
        Sessions.Children.Remove(entry.Control);
        entry.Started.TrySetResult();
    }

    private void OnRestart(object? sender, RoutedEventArgs e)
    {
        if (_current is not { } vm) return;
        RemoveTerminal(vm);
        ShowCurrent();
    }

    private void OnResize(object? sender, VectorEventArgs e)
    {
        var available = TopLevel.GetTopLevel(this)?.Bounds.Height ?? 800;
        Height = Math.Clamp(Height - e.Vector.Y, MinHeight, Math.Max(MinHeight, available * 0.7));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var vm in _entries.Keys.ToArray()) RemoveTerminal(vm);
        base.OnDetachedFromVisualTree(e);
    }
}
