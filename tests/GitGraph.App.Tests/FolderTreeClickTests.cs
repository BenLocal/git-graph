using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitGraph.App.ViewModels;
using GitGraph.App.Views;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.Tests;

/// <summary>Clicking a folder's row in a folder tab's tree opens or closes it, as in VS Code's explorer.</summary>
public sealed class FolderTreeClickTests : IDisposable
{
    private static HeadlessUnitTestSession Ui => TerminalTestApp.Ui;

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("gitgraph-tree-");

    public FolderTreeClickTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir.FullName, "src"));
        File.WriteAllText(Path.Combine(_dir.FullName, "src", "App.cs"), "class App {}\n");
        File.WriteAllText(Path.Combine(_dir.FullName, "README.md"), "# Readme\n");
    }

    public void Dispose()
    {
        try { _dir.Delete(recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// Shows the folder in its view, clicks rows with <paramref name="act"/>, then closes everything.
    /// </summary>
    private Task RunAsync(Action<Window, FolderTabViewModel> act) => Ui.Dispatch<bool>(async () =>
    {
        var host = new FakeHost();
        using var workspace = new WorkspaceViewModel(new WorkspaceConfig { Name = "Tree test" }, host);
        var folder = new FolderTabViewModel(_dir.FullName, workspace, host);
        folder.EnsureLoaded();
        await folder.Loads.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var window = new Window { Width = 900, Height = 600, Content = new FolderTabView { DataContext = folder } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { act(window, folder); }
        finally
        {
            window.Close();
            folder.Dispose();
        }
        return true;
    }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));

    private static FileNodeViewModel Node(FolderTabViewModel folder, string name) => folder.Root.Children.Single(n => n.Name == name);

    /// <summary>A point on a row's name, in window coordinates; <paramref name="dx"/> moves along the name.</summary>
    private static Point OnName(Window window, FileNodeViewModel node, double dx = 0)
    {
        var name = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Classes.Contains("name") && t.DataContext == node);
        var at = name.TranslatePoint(new Point(Math.Min(4 + dx, name.Bounds.Width - 1), name.Bounds.Height / 2), window);
        return at ?? throw new InvalidOperationException("The row is not in the window.");
    }

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task A_click_on_a_folder_opens_it_and_another_click_closes_it() => RunAsync((window, folder) =>
    {
        var src = Node(folder, "src");
        Assert.False(src.IsExpanded);

        Click(window, OnName(window, src));
        Assert.True(src.IsExpanded);
        Assert.Contains(src.Children, c => c.Name == "App.cs");

        // Far enough from the first click that it isn't read as a double click.
        Click(window, OnName(window, src, dx: 12));
        Assert.False(src.IsExpanded);
    });

    [Fact]
    public Task A_double_click_on_a_folder_leaves_it_open() => RunAsync((window, folder) =>
    {
        var src = Node(folder, "src");
        var at = OnName(window, src);

        Click(window, at);
        Click(window, at);
        Assert.True(src.IsExpanded);
    });

    [Fact]
    public Task A_click_on_a_file_selects_it_and_opens_nothing() => RunAsync((window, folder) =>
    {
        var readme = Node(folder, "README.md");

        Click(window, OnName(window, readme));
        Assert.Same(readme, folder.SelectedNode);
        Assert.False(Node(folder, "src").IsExpanded);
    });

    [Fact]
    public Task A_rows_menu_lists_the_open_with_apps() => RunAsync((window, folder) =>
    {
        var readme = Node(folder, "README.md");
        var row = window.GetVisualDescendants().OfType<Grid>().First(g => g.DataContext == readme && g.ContextFlyout is not null);
        var menu = (MenuFlyout)row.ContextFlyout!;

        menu.ShowAt(row);
        Dispatcher.UIThread.RunJobs();
        var openIn = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Open in");
        openIn.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();

        var names = openIn.GetRealizedContainers().OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Contains("VS Code", names);
        Assert.Equal("Edit open-with.toml…", names[^1]);
        menu.Hide();
    });

    [Fact]
    public Task The_toolbar_button_lists_the_open_with_apps_for_the_folder() => RunAsync((window, folder) =>
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(b => ToolTip.GetTip(b) as string == "Open the folder in another app");
        var menu = (MenuFlyout)button.Flyout!;

        menu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();

        var names = menu.Items.OfType<CustomCommandItem>().Select(i => i.Name).ToList();
        Assert.Contains("VS Code", names);
        Assert.Equal("Edit open-with.toml…", names[^1]);
        menu.Hide();
    });
}
