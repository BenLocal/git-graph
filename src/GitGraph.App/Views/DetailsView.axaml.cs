using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GitGraph.App.Controls;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class DetailsView : UserControl
{
    private DetailsViewModel? _vm;

    public DetailsView()
    {
        InitializeComponent();
        FileList.DoubleTapped += OnFileDoubleTapped;
        MessageMenu.Opening += (_, _) => CopySelectionItem.IsEnabled = SelectedMessageText() != null;
        FileList.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not DetailsViewModel vm) return;
            vm.OpenFullDiffCommand.Execute(null);
            e.Handled = true;
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as DetailsViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
    }

    // Show that the content was replaced: the pane for a new selection, the diff for a new file.
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetailsViewModel.Heading)) Motion.FadeIn(ContentRoot);
        else if (e.PropertyName == nameof(DetailsViewModel.DiffLines)) Motion.FadeIn(DiffArea);
    }

    /// <summary>Selection lives in one text block at a time; the menu copies whichever has some.</summary>
    private string? SelectedMessageText() =>
        MessagePage.GetVisualDescendants().OfType<SelectableTextBlock>()
            .Select(t => t.SelectedText).FirstOrDefault(t => !string.IsNullOrEmpty(t));

    private void OnCopySelection(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedMessageText() is { } text && _vm != null) _ = _vm.CopyTextAsync(text);
    }

    private void OnWorkingFileTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DetailsViewModel vm || (sender as Control)?.DataContext is not FileChangeViewModel file) return;
        var mods = e.KeyModifiers;
        vm.SelectWorkingFile(file, toggle: mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta), range: mods.HasFlag(KeyModifiers.Shift));
        WorkingList.Focus();
    }

    /// <summary>Keys in the uncommitted list: arrows move (Shift to extend), Enter opens, Space stages, Delete discards.</summary>
    private void OnWorkingListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DetailsViewModel vm) return;
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Up: vm.MoveWorkingSelection(-1, extend); break;
            case Key.Down: vm.MoveWorkingSelection(1, extend); break;
            case Key.Enter: vm.OpenFullDiffCommand.Execute(null); break;
            case Key.Space: _ = vm.ToggleStageSelectedAsync(); break;
            case Key.Delete: _ = vm.DiscardSelectedAsync(); break;
            default: return;
        }
        e.Handled = true;
        // Keep the selected row in view as the arrows move past the edge.
        WorkingList.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("filerow") && b.DataContext == vm.SelectedFile)?.BringIntoView();
    }

    private void OnWorkingFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DetailsViewModel vm && (sender as Control)?.DataContext is FileChangeViewModel file) vm.OpenFullDiffCommand.Execute(file);
    }

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DetailsViewModel vm) return;
        var file = (e.Source as Control)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(c => c.DataContext).OfType<FileChangeViewModel>().FirstOrDefault();
        if (file != null) vm.OpenFullDiffCommand.Execute(file);
    }
}
