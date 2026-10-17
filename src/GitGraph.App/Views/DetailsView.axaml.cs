using Avalonia.Controls;
using Avalonia.Input;
using System.ComponentModel;
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

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DetailsViewModel vm) return;
        var file = (e.Source as Control)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(c => c.DataContext).OfType<FileChangeViewModel>().FirstOrDefault();
        if (file != null) vm.OpenFullDiffCommand.Execute(file);
    }
}
