using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitGraph.App.ViewModels;
using GitGraph.App.Views;
using GitGraph.Core.Workspaces;

namespace GitGraph.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // GITGRAPH_STATE points the app at a separate state file, e.g. for demos or tests.
            var statePath = Environment.GetEnvironmentVariable("GITGRAPH_STATE") is { Length: > 0 } p ? p : WorkspaceStore.DefaultPath;
            var store = new WorkspaceStore(statePath);
            var vm = new MainWindowViewModel(store);
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += (_, _) => vm.SaveState();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
