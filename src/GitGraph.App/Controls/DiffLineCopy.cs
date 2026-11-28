using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Controls;

/// <summary>
/// Copying from a diff list, whose rows are list items rather than an editor's text: click a line to select it,
/// Shift or Ctrl/Cmd click for more, then the copy shortcut or Copy in the context menu copies the selected lines' code,
/// without line numbers or signs. In a side-by-side list the side that was clicked is copied, and only it is highlighted.
/// </summary>
public static class DiffLineCopy
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("IsEnabled", typeof(DiffLineCopy));

    public static bool GetIsEnabled(ListBox list) => list.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ListBox list, bool value) => list.SetValue(IsEnabledProperty, value);

    static DiffLineCopy()
    {
        IsEnabledProperty.Changed.AddClassHandler<ListBox>((list, e) =>
        {
            if (e.NewValue is not true) return;
            list.SelectionMode = SelectionMode.Multiple;
            list.Classes.Add("copylines");
            list.Classes.Add("copyright");
            list.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            list.KeyDown += OnKeyDown;

            var copy = new MenuItem { Header = "Copy" };
            copy.Click += (_, _) => _ = CopyAsync(list);
            var menu = new MenuFlyout { Items = { copy } };
            menu.Opening += (_, _) => copy.IsEnabled = Text(list).Length > 0;
            list.ContextFlyout = menu;
        });
    }

    /// <summary>Which side of a side-by-side row was pressed decides which side is copied and highlighted.</summary>
    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list || e.Source is not Visual source) return;
        var cell = source.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("cell"));
        if (cell is null) return;
        var left = cell.Classes.Contains("left");
        list.Classes.Set("copyleft", left);
        list.Classes.Set("copyright", !left);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox list || list.GetPlatformSettings()?.HotkeyConfiguration.Copy is not { } gestures) return;
        if (!gestures.Any(g => g.Matches(e))) return;
        e.Handled = true;
        _ = CopyAsync(list);
    }

    private static async Task CopyAsync(ListBox list)
    {
        var text = Text(list);
        if (text.Length > 0 && TopLevel.GetTopLevel(list)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    private static string Text(ListBox list) =>
        list.ItemsSource is System.Collections.IList items
            ? Text(list.Selection.SelectedIndexes.Where(i => i >= 0 && i < items.Count).Order().Select(i => items[i]), left: list.Classes.Contains("copyleft"))
            : "";

    /// <summary>The code of the selected rows, in list order; hunk headers and the empty side of a row are left out.</summary>
    public static string Text(IEnumerable<object?> rows, bool left) => string.Join('\n', rows.Select(row => row switch
    {
        DiffLineViewModel { IsHunk: false } line => line.Text,
        SplitRowViewModel { IsHunk: false } split when (left ? split.Left : split.Right) is { IsEmpty: false } cell => cell.Text,
        _ => null,
    }).OfType<string>());
}
