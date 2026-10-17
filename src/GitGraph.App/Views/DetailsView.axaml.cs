using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class DetailsView : UserControl
{
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

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DetailsViewModel vm) return;
        var file = (e.Source as Control)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(c => c.DataContext).OfType<FileChangeViewModel>().FirstOrDefault();
        if (file != null) vm.OpenFullDiffCommand.Execute(file);
    }
}
