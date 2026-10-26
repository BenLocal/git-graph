using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>
/// Keyboard shortcuts: every command in <see cref="ShortcutService"/> runs from here, so a changed
/// shortcut works everywhere at once. Two-key chords (Ctrl+K …) wait a few seconds for the second key.
/// </summary>
public partial class MainWindow
{
    private Dictionary<string, Action>? _actions;
    private List<ShortcutCommand>? _chordCandidates;
    private DispatcherTimer? _chordTimeout;

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;
    private WorkspaceViewModel? Workspace => Vm?.SelectedWorkspace;
    private RepoTabViewModel? Tab => Workspace?.SelectedRepoTab;

    private void InitShortcuts()
    {
        // Tunnel, so shortcuts reach the window before a list or text box swallows the key.
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
        _actions = new Dictionary<string, Action>
        {
            ["ToggleSidebar"] = () => Vm?.ToggleSidebarCommand.Execute(null),
            ["TogglePanel"] = () => Vm?.TogglePanelCommand.Execute(null),
            ["MaximizePanel"] = () => Vm?.ToggleMaximizePanelCommand.Execute(null),
            ["ToggleDiffLayout"] = () => { if (Tab is { } t) t.Details.IsSplit = !t.Details.IsSplit; },
            ["CycleTheme"] = () => Vm?.CycleThemeCommand.Execute(null),
            ["FontLarger"] = () => Vm?.FontLargerCommand.Execute(null),
            ["FontSmaller"] = () => Vm?.FontSmallerCommand.Execute(null),
            ["FontReset"] = () => Vm?.FontResetCommand.Execute(null),
            ["Find"] = FocusFind,
            ["FindNext"] = () => Tab?.FindNextCommand.Execute(null),
            ["FindPrevious"] = () => Tab?.FindPreviousCommand.Execute(null),
            ["Refresh"] = () => Tab?.RefreshCommand.Execute(null),
            ["GoToHead"] = () => Tab?.GoToHeadCommand.Execute(null),
            ["ToggleContributors"] = () => Tab?.ToggleStatsCommand.Execute(null),
            ["Fetch"] = () => Tab?.FetchCommand.Execute(null),
            ["Pull"] = () => Tab?.PullCommand.Execute(null),
            ["Push"] = () => Tab?.PushCommand.Execute(null),
            ["Sync"] = () => Tab?.SyncNowCommand.Execute(null),
            ["UndoLastAction"] = () => Tab?.UndoLastActionCommand.Execute(null),
            ["ShowReflog"] = () => Tab?.ShowReflogCommand.Execute(null),
            ["CloseTab"] = () => { if (Tab is { } t) Workspace!.CloseTabCommand.Execute(t); },
            ["CloseAllTabs"] = () => Workspace?.CloseAllTabsCommand.Execute(null),
            ["KeepTabOpen"] = () => { if (Tab is { } t) Workspace!.PinTabCommand.Execute(t); },
            ["NextTab"] = () => MoveTab(+1),
            ["PreviousTab"] = () => MoveTab(-1),
            ["AddFolder"] = () => Workspace?.AddFolderCommand.Execute(null),
            ["NewWorkspace"] = () => Vm?.NewWorkspaceCommand.Execute(null),
            ["KeyboardShortcuts"] = OpenKeyboardShortcuts,
            ["Quit"] = Close,
        };
        for (var n = 1; n <= 9; n++)
        {
            var index = n - 1;
            _actions[$"Tab{n}"] = () =>
            {
                if (Workspace is { } ws && index < ws.Tabs.Count) ws.SelectedTab = ws.Tabs[index];
            };
        }
    }

    public void RunShortcut(string id)
    {
        if (_actions?.TryGetValue(id, out var action) == true) action();
    }

    /// <summary>Find commits in a repository tab, or search the open file in a folder tab.</summary>
    private void FocusFind()
    {
        if (this.GetVisualDescendants().OfType<FolderTabView>().FirstOrDefault(v => v.IsEffectivelyVisible) is { } folder)
            folder.OpenSearch();
        else
            this.GetVisualDescendants().OfType<RepoTabView>().FirstOrDefault(v => v.IsEffectivelyVisible)?.FocusFind();
    }

    private void MoveTab(int step)
    {
        if (Workspace is not { Tabs.Count: > 0 } ws) return;
        var i = ws.SelectedTab is { } t ? ws.Tabs.IndexOf(t) : 0;
        ws.SelectedTab = ws.Tabs[((i + step) % ws.Tabs.Count + ws.Tabs.Count) % ws.Tabs.Count];
    }

    private void OpenKeyboardShortcuts() => new KeyboardShortcutsWindow().ShowDialog(this);

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        // Dialogs and the shortcut recorder handle their own keys.
        if (e.Source is Visual v && TopLevel.GetTopLevel(v) != this) return;
        // Shells and terminal applications own their keys, including Ctrl+C, Ctrl+R and Tab.
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<TerminalPanelView>().Any()) return;
        if (ShortcutService.FromKeyEvent(e) is null) return;
        var svc = ShortcutService.Instance;

        if (_chordCandidates != null)
        {
            var second = _chordCandidates.FirstOrDefault(c => svc.Parts(c.Id) is { Count: 2 } p && p[1].Matches(e));
            EndChord();
            e.Handled = true;
            if (second != null) RunShortcut(second.Id);
            else ShowChordHint($"That key combination isn't a command.", 1.5);
            return;
        }

        // Plain keys and Shift+key type text in a text box; only Ctrl/Alt combinations and F-keys act there.
        var typing = e.Source is TextBox && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
                     && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is not (>= Key.F1 and <= Key.F24);
        if (typing) return;

        var single = svc.Commands.FirstOrDefault(c => svc.Parts(c.Id) is { Count: 1 } p && p[0].Matches(e));
        // Commands of other windows (the diff window's) don't take the key here.
        if (single != null && _actions?.ContainsKey(single.Id) == true)
        {
            RunShortcut(single.Id);
            e.Handled = true;
            return;
        }
        var chords = svc.Commands.Where(c => svc.Parts(c.Id) is { Count: 2 } p && p[0].Matches(e)).ToList();
        if (chords.Count == 0) return;
        _chordCandidates = chords;
        e.Handled = true;
        ShowChordHint($"({ShortcutService.Format(svc.Parts(chords[0].Id)[0])}) was pressed. Waiting for the second key…", 3);
    }

    private void ShowChordHint(string text, double seconds)
    {
        ChordHint.Text = text;
        ChordHintBorder.Classes.Set("show", true);
        _chordTimeout?.Stop();
        _chordTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _chordTimeout.Tick += (_, _) => EndChord();
        _chordTimeout.Start();
    }

    private void EndChord()
    {
        _chordCandidates = null;
        _chordTimeout?.Stop();
        ChordHintBorder.Classes.Set("show", false);
    }
}
