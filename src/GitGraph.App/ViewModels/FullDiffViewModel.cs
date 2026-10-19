using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGraph.App.Services;
using GitGraph.Core.Diff;
using GitGraph.Core.Git;

namespace GitGraph.App.ViewModels;

/// <summary>
/// A file's diff shown side by side in its own window: the whole file, with long unchanged
/// stretches folded into rows the user can open and close.
/// </summary>
public sealed partial class FullDiffViewModel : ObservableObject
{
    private readonly Services.IDialogService _dialogs;
    private readonly string _fullPath;

    public FullDiffViewModel(FileChange file, string range, FileDiff diff, DiffSyntax syntax, string? message, Services.IDialogService dialogs, string fullPath)
    {
        _dialogs = dialogs;
        _fullPath = fullPath;
        Path = file.Path;
        OldPath = file.OldPath;
        Range = range;
        AddText = diff.Additions > 0 ? $"+{diff.Additions}" : null;
        DelText = diff.Deletions > 0 ? $"−{diff.Deletions}" : null;
        Message = message;
        foreach (var (start, count, foldable) in TextDiff.Sections(diff.AllRows))
        {
            var rows = diff.AllRows.Skip(start).Take(count).Select(r => new SplitRowViewModel(r, syntax)).ToList();
            if (foldable)
            {
                var fold = new FoldViewModel(rows);
                _folds.Add(fold);
                Items.Add(fold);
            }
            else
                foreach (var r in rows) Items.Add(r);
        }
    }

    private readonly List<FoldViewModel> _folds = [];

    public string Path { get; }
    public string? OldPath { get; }
    public bool HasOldPath => OldPath != null;
    public string Range { get; }
    public string? AddText { get; }
    public string? DelText { get; }
    public string? Message { get; }
    public bool HasMessage => Message != null;
    public bool HasFolds => _folds.Count > 0;

    /// <summary>Diff rows and fold rows in display order; opening a fold inserts its rows after it.</summary>
    public ObservableCollection<object> Items { get; } = [];

    [ObservableProperty]
    private bool _isCopied;

    [RelayCommand]
    private Task CopyPath() => CopyAsync(Path);

    [RelayCommand]
    private Task CopyFullPath() => CopyAsync(_fullPath);

    private async Task CopyAsync(string text)
    {
        await _dialogs.CopyTextAsync(text);
        // The copy button shows a check mark for a moment.
        IsCopied = true;
        await Task.Delay(1400);
        IsCopied = false;
    }

    [RelayCommand]
    private void ToggleFold(FoldViewModel fold) => SetExpanded(fold, !fold.IsExpanded);

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var f in _folds) SetExpanded(f, true);
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var f in _folds) SetExpanded(f, false);
    }

    private void SetExpanded(FoldViewModel fold, bool expanded)
    {
        if (fold.IsExpanded == expanded) return;
        var index = Items.IndexOf(fold);
        if (expanded)
            for (var i = 0; i < fold.Rows.Count; i++) Items.Insert(index + 1 + i, fold.Rows[i]);
        else
            for (var i = 0; i < fold.Rows.Count; i++) Items.RemoveAt(index + 1);
        fold.IsExpanded = expanded;
    }
}

/// <summary>A stretch of unchanged lines that can be shown or hidden.</summary>
public sealed partial class FoldViewModel(IReadOnlyList<SplitRowViewModel> rows) : ObservableObject
{
    public IReadOnlyList<SplitRowViewModel> Rows { get; } = rows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isExpanded;

    public string Label => IsExpanded
        ? $"Hide {Rows.Count} unchanged lines"
        : $"{Rows.Count} unchanged lines";
}
