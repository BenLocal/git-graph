using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;
using GitGraph.App.Views;
using GitGraph.Core.Workspaces;
using Iciclecreek.Terminal;

namespace GitGraph.App.Tests;

public static class TerminalTestApp
{
    internal static readonly HeadlessUnitTestSession Ui = HeadlessUnitTestSession.StartNew(typeof(TerminalTestApp), AvaloniaTestIsolationLevel.PerAssembly);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class TerminalTests
{
    private static HeadlessUnitTestSession Ui => TerminalTestApp.Ui;

    [Fact]
    public async Task Missing_directory_does_not_start_a_shell()
    {
        using var session = new TerminalSession();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => session.StartAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), 80, 24));
    }

    [Fact]
    public Task Embedded_shell_has_a_pty_and_directory_and_accepts_interrupt_and_resize() => Ui.Dispatch<bool>(async () =>
    {
        if (OperatingSystem.IsWindows()) return true; // The assertions below use POSIX shell commands.
        var directory = Directory.CreateTempSubdirectory("gitgraph pty space-");
        var vm = new TerminalViewModel(directory.FullName);
        var panel = new TerminalPanelView { DataContext = vm };
        var window = new Window { Width = 900, Height = 600, Content = panel };
        try
        {
            window.Show();
            vm.ShowCommand.Execute(null);
            window.UpdateLayout();
            await panel.WhenStartedAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var control = Assert.Single(panel.GetVisualDescendants().OfType<TerminalControl>());
            Assert.True(control.IsLive, vm.Status);
            await OutputAfter(control, "printf '__INITIAL_SIZE_%s__\\n' \"$(stty size)\"\r",
                $"__INITIAL_SIZE_{control.Terminal.Rows} {control.Terminal.Cols}__");
            var shellPath = "'" + directory.FullName.Replace("'", "'\\''") + "'";
            await OutputAfter(control,
                $"test -t 0 && test -t 1 && test \"$PWD\" -ef {shellPath} && printf '__PTY_%s__\\n' OK\r", "__PTY_OK__");
            await OutputAfter(control, "printf '__UTF8_%s__\\n' '中文'\r", "__UTF8_中文__");
            await OutputAfter(control, "printf '\\033[?1049h__ALT_%s__\\n' ON\r", "__ALT_ON__");
            Assert.True(control.Terminal.IsAlternateBufferActive);
            await OutputAfter(control, "printf '\\033[?1049l__ALT_%s__\\n' OFF\r", "__ALT_OFF__");
            Assert.False(control.Terminal.IsAlternateBufferActive);

            await OutputAfter(control, "printf '__SLEEP_%s__\\n' READY; sleep 30\r", "__SLEEP_READY__");
            var input = Assert.Single(control.GetVisualDescendants().OfType<TerminalView>());
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control });
            await OutputAfter(control, "printf '__INTERRUPT_%s__\\n' OK\r", "__INTERRUPT_OK__");

            window.Width = 650;
            window.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await OutputAfter(control, "printf '__SIZE_%s__\\n' \"$(stty size)\"\r",
                $"__SIZE_{control.Terminal.Rows} {control.Terminal.Cols}__");
        }
        finally
        {
            vm.CloseCommand.Execute(null);
            window.Close();
            directory.Delete(true);
        }
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Ime_keys_stay_with_the_input_method_and_committed_text_reaches_the_shell() => Ui.Dispatch<bool>(async () =>
    {
        var vm = new TerminalViewModel(Path.GetTempPath());
        var panel = new TerminalPanelView { DataContext = vm };
        var window = new Window { Width = 900, Height = 600, Content = panel };
        try
        {
            window.Show();
            vm.ShowCommand.Execute(null);
            window.UpdateLayout();
            await panel.WhenStartedAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var control = Assert.Single(panel.GetVisualDescendants().OfType<TerminalControl>());
            var input = Assert.Single(control.GetVisualDescendants().OfType<TerminalView>());
            input.Focus();
            var sent = new StringBuilder();
            control.InputSent += (_, text) => sent.Append(text);
            foreach (var symbol in new[] { "n", "i", " ", "1" })
            {
                var key = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.ImeProcessed,
                    KeySymbol = symbol,
                };
                input.RaiseEvent(key);
                Assert.False(key.Handled);
            }
            Assert.Empty(sent.ToString());
            var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            control.InputSent += (_, text) => { if (text == "你好") committed.TrySetResult(); };
            input.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "你好" });
            await committed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("你好", sent.ToString());
        }
        finally
        {
            vm.CloseCommand.Execute(null);
            window.Close();
        }
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Switching_and_hiding_keep_sessions_but_closing_a_tab_ends_its_shell() => Ui.Dispatch<bool>(async () =>
    {
        using var workspace = new WorkspaceViewModel(new WorkspaceConfig { Name = "Terminal test" }, new FakeHost());
        var directory = Directory.CreateTempSubdirectory("gitgraph-terminal-");
        var other = Directory.CreateDirectory(Path.Combine(directory.FullName, "other"));
        var panel = new TerminalPanelView();
        var window = new Window { Width = 900, Height = 600, Content = panel };
        try
        {
            window.Show();
            workspace.OpenTerminal(directory.FullName, folder: true);
            var first = workspace.SelectedTab!;
            Assert.False(first.IsPreview);
            panel.DataContext = first.Terminal;
            window.UpdateLayout();
            await panel.WhenStartedAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var control = Assert.Single(panel.GetVisualDescendants().OfType<TerminalControl>());
            Assert.True(control.IsLive, first.Terminal.Status);
            var pid = control.Pid;

            first.Terminal.HideCommand.Execute(null);
            Assert.False(panel.IsVisible);
            Assert.True(control.IsLive);
            first.Terminal.ShowCommand.Execute(null);
            Assert.Equal(pid, control.Pid);

            workspace.OpenTerminal(other.FullName, folder: true);
            panel.DataContext = workspace.SelectedTab!.Terminal;
            window.UpdateLayout();
            await panel.WhenStartedAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(2, panel.GetVisualDescendants().OfType<TerminalControl>().Count());
            Assert.False(control.IsVisible);
            Assert.True(control.IsLive);

            workspace.SelectedTab = first;
            panel.DataContext = first.Terminal;
            Assert.True(control.IsVisible);
            Assert.Equal(pid, control.Pid);
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            workspace.CloseTabCommand.Execute(first);
            Assert.DoesNotContain(control, panel.GetVisualDescendants().OfType<TerminalControl>());
            Assert.False(control.IsLive);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            window.Close();
            directory.Delete(true);
        }
        return true;
    }, CancellationToken.None);

    private static async Task OutputAfter(TerminalControl control, string command, string marker)
    {
        var output = new StringBuilder();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOutput(object? sender, OutputReceivedEventArgs e)
        {
            output.Append(e.Output);
            if (output.ToString().Contains(marker, StringComparison.Ordinal)) received.TrySetResult();
        }
        control.OutputReceived += OnOutput;
        try
        {
            await control.SendInputAsync(command);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Waiting for {marker}. Terminal output: {output}");
        }
        finally { control.OutputReceived -= OnOutput; }
    }
}
