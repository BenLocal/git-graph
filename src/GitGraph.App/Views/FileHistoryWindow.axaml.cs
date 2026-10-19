using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitGraph.App.Controls;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>One file's history and blame, in its own maximized window.</summary>
public partial class FileHistoryWindow : Window
{
    public FileHistoryWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Motion.FadeIn(Content as Control);
        HistoryList.AddHandler(ScrollViewer.ScrollChangedEvent, OnHistoryScrolled);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Infinite scroll: the next page loads before the end of the list comes into view.</summary>
    private void OnHistoryScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is ScrollViewer sv && DataContext is FileHistoryViewModel vm && sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 400)
            _ = vm.LoadMoreAsync();
    }

    private void OnBlameTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is BlameRowViewModel line) line.Owner.ShowCommitCommand.Execute(line);
    }
}
