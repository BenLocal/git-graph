namespace GitGraph.App.Services;

public sealed record PromptField(string Label, string Value = "", string? Placeholder = null, bool IsCheckbox = false, string? Hint = null);

/// <summary>UI interactions view models ask for; implemented by the main window.</summary>
public interface IDialogService
{
    /// <returns>The entered values in field order (checkboxes as "true"/"false"), or null when cancelled.</returns>
    Task<string[]?> PromptAsync(string title, string? message, IReadOnlyList<PromptField> fields, string confirmText);

    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false);

    Task<string?> PickFolderAsync(string title);

    Task CopyTextAsync(string text);

    /// <summary>Shows one file's diff side by side in a maximized window.</summary>
    void ShowDiff(ViewModels.FullDiffViewModel diff);
}
