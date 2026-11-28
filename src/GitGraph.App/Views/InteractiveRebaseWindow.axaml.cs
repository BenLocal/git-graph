using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GitGraph.App.Controls;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>Plans an interactive rebase. Rows are reordered by dragging their handle; the list moves as the pointer does.</summary>
public partial class InteractiveRebaseWindow : ShadUI.Window
{
    private RebaseRowViewModel? _dragging;
    private InteractiveRebaseViewModel? _vm;

    public InteractiveRebaseWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Motion.FadeIn(Content as Control);
        Closed += (_, _) =>
        {
            if (_vm != null) _vm.CloseRequested -= Close;
            _vm = null;
        };
        AddHandler(PointerCaptureLostEvent, (_, _) => _dragging = null);
    }

    private InteractiveRebaseViewModel? Vm => DataContext as InteractiveRebaseViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.CloseRequested -= Close;
        _vm = Vm;
        if (_vm != null) _vm.CloseRequested += Close;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape) Close();
        // Alt+↑/↓ moves the selected commit, as Alt moves lines in VS Code.
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down && Vm is { Selected: { } row } vm)
        {
            vm.Move(row, vm.Rows.IndexOf(row) + (e.Key == Key.Up ? -1 : 1));
            e.Handled = true;
        }
    }

    // A drag stays inside this list, so it follows the pointer directly rather than through the platform's drag and drop.
    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((sender as Control)?.DataContext is not RebaseRowViewModel row) return;
        _dragging = row;
        Vm!.Selected = row;
        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || Vm is not { } vm) return;
        var hit = List.InputHitTest(e.GetPosition(List)) as Visual;
        var item = hit?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (item?.DataContext is not RebaseRowViewModel target || target == _dragging) return;
        var from = vm.Rows.IndexOf(_dragging);
        var to = vm.Rows.IndexOf(target);
        if (from < 0 || to < 0) return;
        // Require crossing the target row's midpoint so mixed-height rows (e.g. expanded Reword) don't oscillate.
        var y = e.GetPosition(item).Y;
        var mid = item.Bounds.Height / 2;
        if ((to > from && y >= mid) || (to < from && y <= mid))
            vm.Move(_dragging, to);
    }

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = null;
        e.Pointer.Capture(null);
    }
}
