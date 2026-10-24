using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace GitGraph.App.Views;

/// <summary>What a custom command printed, to read, select and copy.</summary>
public sealed class OutputWindow : Window
{
    public OutputWindow(string title, string text)
    {
        Title = $"{title} – Git Graph";
        Width = 820;
        Height = 520;
        MinWidth = 400;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable("SurfaceBrush").ToBinding();

        var copy = new Button { Content = "Copy", Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        };
        var close = new Button { Content = "Close", Classes = { "primary" } };
        close.Click += (_, _) => Close();

        var output = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 12) };
        output[!SelectableTextBlock.FontFamilyProperty] = this.GetResourceObservable("CodeFont").ToBinding();
        Content = new DockPanel
        {
            Children =
            {
                new TextBlock { Text = title, Classes = { "title" }, Margin = new Thickness(20, 14, 20, 4), [DockPanel.DockProperty] = Dock.Top },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 10),
                    [DockPanel.DockProperty] = Dock.Bottom, Children = { copy, close },
                },
                new ScrollViewer { Content = output },
            },
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }
}
