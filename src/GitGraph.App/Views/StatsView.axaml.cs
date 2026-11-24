using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace GitGraph.App.Views;

public partial class StatsView : UserControl
{
    public static readonly StyledProperty<ICommand?> BackCommandProperty =
        AvaloniaProperty.Register<StatsView, ICommand?>(nameof(BackCommand));

    public ICommand? BackCommand
    {
        get => GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public StatsView() => InitializeComponent();
}
