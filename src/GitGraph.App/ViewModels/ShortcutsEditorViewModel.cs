using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;

namespace GitGraph.App.ViewModels;

/// <summary>The Keyboard Shortcuts window: every command, its shortcut, and whether it differs from the default.</summary>
public sealed partial class ShortcutsEditorViewModel : ObservableObject
{
    private readonly ShortcutService _svc = ShortcutService.Instance;

    public ShortcutsEditorViewModel()
    {
        _svc.Changed += Reload;
        Reload();
    }

    public ObservableCollection<ShortcutRowViewModel> Rows { get; } = [];

    /// <summary>Matches the command's name, its group, or its shortcut ("ctrl+k").</summary>
    [ObservableProperty]
    private string _filter = "";

    partial void OnFilterChanged(string value) => Reload();

    public void Detach() => _svc.Changed -= Reload;

    private void Reload()
    {
        Rows.Clear();
        var f = Filter.Trim();
        foreach (var c in _svc.Commands)
        {
            var row = new ShortcutRowViewModel(c, _svc);
            if (f.Length == 0 || c.Title.Contains(f, StringComparison.OrdinalIgnoreCase)
                || c.Category.Contains(f, StringComparison.OrdinalIgnoreCase)
                || row.Label.Contains(f, StringComparison.OrdinalIgnoreCase))
                Rows.Add(row);
        }
    }

    [RelayCommand]
    private void Reset(ShortcutRowViewModel row) => _svc.Reset(row.Command.Id);

    [RelayCommand]
    private void Remove(ShortcutRowViewModel row) => _svc.Set(row.Command.Id, null);

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var id in _svc.Overrides.Keys.ToList()) _svc.Reset(id);
    }

    public void Set(ShortcutRowViewModel row, string gesture) => _svc.Set(row.Command.Id, gesture);
}

public sealed class ShortcutRowViewModel(ShortcutCommand command, ShortcutService svc)
{
    public ShortcutCommand Command { get; } = command;
    public string Title => Command.Title;
    public string Category => Command.Category;
    public string Label { get; } = ShortcutService.Format(svc.Gesture(command.Id));
    public bool HasShortcut => Label.Length > 0;
    public bool IsChanged { get; } = !svc.IsDefault(command.Id);
    public string? DefaultLabel { get; } = command.DefaultGesture is { } d ? $"Default: {ShortcutService.Format(d)}" : "No default";
    public string? Conflict { get; } = svc.Conflicts(command.Id).Select(c => c.Title).FirstOrDefault() is { } other
        ? $"Also used by {other}"
        : null;
    public bool HasConflict => Conflict != null;
}
