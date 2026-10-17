using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class RepoTabView : UserControl
{
    public RepoTabView()
    {
        InitializeComponent();
        // Tunnel so Ctrl+click is seen before the ListBox turns it into a selection toggle.
        CommitList.AddHandler(PointerPressedEvent, OnCommitPointerPressed, RoutingStrategies.Tunnel);
        CommitList.SelectionChanged += (_, _) =>
        {
            if (CommitList.SelectedItem is { } item) CommitList.ScrollIntoView(item);
        };
    }

    public void FocusFind()
    {
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void OnCommitPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not RepoTabViewModel vm) return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || !e.GetCurrentPoint(CommitList).Properties.IsLeftButtonPressed) return;
        var row = (e.Source as Visual)?.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(c => c.DataContext)
            .OfType<CommitRowViewModel>()
            .FirstOrDefault();
        if (row is null) return;
        vm.SetCompare(row);
        e.Handled = true;
    }
}
