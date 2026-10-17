using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GitGraph.App.Services;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class MainWindow : Window, IDialogService
{
    private DateTime _lastActivated = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        Activated += (_, _) =>
        {
            // Files may have changed in an editor while we were in the background.
            if (DateTime.UtcNow - _lastActivated < TimeSpan.FromSeconds(2)) return;
            _lastActivated = DateTime.UtcNow;
            (DataContext as MainWindowViewModel)?.SelectedWorkspace.SelectedTab?.OnActivated();
        };
        KeyDown += OnWindowKeyDown;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm) vm.Dialogs = this;
    }

    private void OnExit(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnFind(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        this.GetVisualDescendants().OfType<RepoTabView>().FirstOrDefault(v => v.IsEffectivelyVisible)?.FocusFind();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            this.GetVisualDescendants().OfType<RepoTabView>().FirstOrDefault(v => v.IsEffectivelyVisible)?.FocusFind();
            e.Handled = true;
        }
    }

    public Task<string[]?> PromptAsync(string title, string? message, IReadOnlyList<PromptField> fields, string confirmText) =>
        new PromptDialog(title, message, fields, confirmText, destructive: false).ShowDialog<string[]?>(this);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false) =>
        await new PromptDialog(title, message, [], confirmText, destructive).ShowDialog<string[]?>(this) is not null;

    public async Task<string?> PickFolderAsync(string title)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task CopyTextAsync(string text)
    {
        if (Clipboard is { } cb) await cb.SetTextAsync(text);
    }

    public void ShowDiff(FullDiffViewModel diff) => new DiffWindow { DataContext = diff }.Show(this);
}
