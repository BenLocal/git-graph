using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class KeyboardShortcutsWindow : ShadUI.Window
{
    private readonly ShortcutsEditorViewModel _vm = new();
    private bool _recording;

    public KeyboardShortcutsWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Closed += (_, _) => _vm.Detach();
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
        if (_recording) return;
        _recording = true;
        try
        {
            var gesture = await new KeyCaptureDialog(row.Command.Id, row.Title).ShowDialog<string?>(this);
            if (gesture != null) _vm.Set(row, gesture);
        }
        finally { _recording = false; }
    }

    private static ShortcutRowViewModel? RowFrom(object? source) =>
        (source as Control)?.GetSelfAndVisualAncestors().OfType<Control>()
        .Select(c => c.DataContext).OfType<ShortcutRowViewModel>().FirstOrDefault();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape) Close();
    }
}
