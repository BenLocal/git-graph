using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GitGraph.App.Controls;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>Plans an interactive rebase. Rows are reordered by dragging their handle; the list moves as the pointer does.</summary>
public partial class InteractiveRebaseWindow : Window
{
    private RebaseRowViewModel? _dragging;

    public InteractiveRebaseWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Motion.FadeIn(Content as Control);
    }

    private InteractiveRebaseViewModel? Vm => DataContext as InteractiveRebaseViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is { } vm) vm.CloseRequested += Close;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
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
        if ((sender as Control)?.DataContext is not RebaseRowViewModel row) return;
        _dragging = row;
        Vm!.Selected = row;
        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || Vm is not { } vm) return;
        // The row under the pointer: the dragged one takes its place.
        var hit = List.InputHitTest(e.GetPosition(List)) as Visual;
        var target = hit?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as RebaseRowViewModel;
        if (target != null && target != _dragging) vm.Move(_dragging, vm.Rows.IndexOf(target));
    }

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = null;
        e.Pointer.Capture(null);
    }
}
