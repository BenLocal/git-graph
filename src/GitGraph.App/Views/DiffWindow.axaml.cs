using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>One file's diff, side by side, in its own maximized window.</summary>
public partial class DiffWindow : Window
{
    public DiffWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Motion.FadeIn(Content as Control);
        // Tunnel, so the keys reach the window before the editors.
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (DataContext is not FullDiffViewModel vm) return;
        var svc = ShortcutService.Instance;
        bool Is(string id) => svc.Parts(id) is { Count: 1 } p && p[0].Matches(e);
        if (Is("NextChange")) vm.NextChangeCommand.Execute(null);
        else if (Is("PreviousChange")) vm.PreviousChangeCommand.Execute(null);
        else return;
        e.Handled = true;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
