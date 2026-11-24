using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>File → Custom commands.</summary>
public partial class CustomCommandsWindow : Window
{
    public CustomCommandsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as CustomCommandsViewModel)?.FlushSave();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape) Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
