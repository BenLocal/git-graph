using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class KeyboardShortcutsWindow : Window
{
    private readonly ShortcutsEditorViewModel _vm = new();

    public KeyboardShortcutsWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Closed += (_, _) => _vm.Detach();
        Opened += (_, _) => Services.WaylandAppId.Apply(this);
        List.DoubleTapped += (_, e) =>
        {
            if (RowFrom(e.Source) is { } row) _ = RecordAsync(row);
        };
    }

    private void OnChange(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ShortcutRowViewModel row) _ = RecordAsync(row);
    }

    private async Task RecordAsync(ShortcutRowViewModel row)
    {
        var gesture = await new KeyCaptureDialog(row.Title).ShowDialog<string?>(this);
        if (gesture != null) _vm.Set(row, gesture);
    }

    private static ShortcutRowViewModel? RowFrom(object? source) =>
        (source as Control)?.GetSelfAndVisualAncestors().OfType<Control>()
        .Select(c => c.DataContext).OfType<ShortcutRowViewModel>().FirstOrDefault();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }
}
