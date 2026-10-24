using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitGraph.App.Controls;

namespace GitGraph.App.Views;

/// <summary>A repository's reflog, in a window of its own.</summary>
public partial class ReflogWindow : Window
{
    public ReflogWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Motion.FadeIn(Content as Control);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
