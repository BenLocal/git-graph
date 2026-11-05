using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitGraph.App.Controls;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>A folder's files in a window of its own; closing the window closes the folder and its terminal.</summary>
public partial class FolderWindow : Window
{
    private DateTime _lastActivated;
    private readonly Dictionary<string, Action> _actions;

    public FolderWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            _lastActivated = DateTime.UtcNow;
            Motion.FadeIn(Content as Control);
        };
        // Files may have changed in an editor while the window was in the background, as for the main window.
        Activated += (_, _) =>
        {
            if (DateTime.UtcNow - _lastActivated < TimeSpan.FromSeconds(2)) return;
            _lastActivated = DateTime.UtcNow;
            (DataContext as FolderTabViewModel)?.OnActivated();
        };
        Closed += (_, _) => (DataContext as FolderTabViewModel)?.Dispose();
        // The folder view's shortcuts; the rest belong to the main window.
        _actions = new Dictionary<string, Action>
        {
            ["Find"] = Folder.OpenSearch,
            ["GoToDefinition"] = () => Folder.Navigate(NavKind.Definition),
            ["FindReferences"] = () => Folder.Navigate(NavKind.References),
            ["GoToImplementation"] = () => Folder.Navigate(NavKind.Implementations),
            ["NavigateBack"] = () => (DataContext as FolderTabViewModel)?.GoBackCommand.Execute(null),
            ["NavigateForward"] = () => (DataContext as FolderTabViewModel)?.GoForwardCommand.Execute(null),
        };
        // Tunnel, so the keys reach the window before the editor or the tree takes them.
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        // Shells and terminal applications own their keys.
        if (e.Source is Avalonia.Visual source && Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(source).OfType<TerminalPanelView>().Any()) return;
        var svc = ShortcutService.Instance;
        foreach (var (id, action) in _actions)
        {
            if (svc.Parts(id) is not [var gesture] || !gesture.Matches(e)) continue;
            action();
            e.Handled = true;
            return;
        }
    }
}
