using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GitGraph.App.Views;

/// <summary>File → Custom commands.</summary>
public partial class CustomCommandsWindow : Window
{
    public CustomCommandsWindow() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
