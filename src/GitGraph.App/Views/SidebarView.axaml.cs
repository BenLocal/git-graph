using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        InitializeComponent();
        // A single click opens a preview tab (the buttons' own command); a double-click keeps it open.
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
    }

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var node = (e.Source as Control)?.GetSelfAndVisualAncestors().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("nav"))?.DataContext;
        switch (node)
        {
            case RepoNodeViewModel repo: repo.PinCommand.Execute(null); break;
            case WorktreeNodeViewModel wt: wt.PinCommand.Execute(null); break;
            case FolderNodeViewModel folder: folder.PinCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }
}
