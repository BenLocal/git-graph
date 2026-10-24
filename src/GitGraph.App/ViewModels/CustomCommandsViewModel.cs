using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.Core.Workspaces;

namespace GitGraph.App.ViewModels;

/// <summary>File → Custom commands: the user's own commands for the context menus; every change is saved at once.</summary>
public sealed partial class CustomCommandsViewModel : ObservableObject
{
    private readonly List<CustomCommand> _commands;
    private readonly Action _save;

    public CustomCommandsViewModel(List<CustomCommand> commands, Action save)
    {
        _commands = commands;
        _save = save;
        foreach (var c in commands) Items.Add(new CustomCommandEditViewModel(c, save));
        _selected = Items.FirstOrDefault();
    }

    public static IReadOnlyList<string> Scopes { get; } = ["Commit", "Branch", "File", "Repository"];

    public ObservableCollection<CustomCommandEditViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private CustomCommandEditViewModel? _selected;

    public bool HasSelection => Selected != null;

    [RelayCommand]
    private void Add()
    {
        var command = new CustomCommand { Name = "New command", Scope = "Commit", Command = "echo ${short} ${subject}" };
        _commands.Add(command);
        var item = new CustomCommandEditViewModel(command, _save);
        Items.Add(item);
        Selected = item;
        _save();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (Selected is not { } item) return;
        var index = Items.IndexOf(item);
        _commands.Remove(item.Command);
        Items.Remove(item);
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
        _save();
    }
}

/// <summary>One command being edited; each change is written to the state and saved.</summary>
public sealed partial class CustomCommandEditViewModel(CustomCommand command, Action save) : ObservableObject
{
    public CustomCommand Command { get; } = command;

    public string Name
    {
        get => Command.Name;
        set { Command.Name = value; Changed(); }
    }

    public string Scope
    {
        get => Command.Scope;
        set { Command.Scope = value; Changed(); }
    }

    public string Text
    {
        get => Command.Command;
        set { Command.Command = value; Changed(); }
    }

    public bool Confirm
    {
        get => Command.Confirm;
        set { Command.Confirm = value; Changed(); }
    }

    private void Changed()
    {
        OnPropertyChanged(string.Empty);
        save();
    }
}
