using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit.Document;
using GitGraph.App.Controls;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

/// <summary>One file's history and blame, in its own maximized window.</summary>
public partial class FileHistoryWindow : ShadUI.Window
{
    private readonly BlameMargin _blameMargin;
    private FileHistoryViewModel? _vm;
    private IReadOnlyList<BlameRowViewModel> _blameRows = [];

    public FileHistoryWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Motion.FadeIn(Content as Control);
        HistoryList.AddHandler(ScrollViewer.ScrollChangedEvent, OnHistoryScrolled);

        _blameMargin = new BlameMargin(row => row.Owner.ShowCommitCommand.Execute(row));
        BlameEditor.TextArea.LeftMargins.Insert(0, _blameMargin);
        BlameEditor.TextArea.TextView.LineTransformers.Add(new SyntaxColours(BlameEditor, number =>
            number >= 1 && number <= _blameRows.Count ? _blameRows[number - 1].Syntax : null));
        BlameEditor.ActualThemeVariantChanged += (_, _) => ApplyBlameTheme();
        ApplyBlameTheme();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as FileHistoryViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmChanged;
            LoadBlameRows(_vm.BlameLines);
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileHistoryViewModel.BlameLines) && _vm != null)
            LoadBlameRows(_vm.BlameLines);
    }

    private void LoadBlameRows(IReadOnlyList<BlameRowViewModel> rows)
    {
        _blameRows = rows;
        BlameEditor.Document = new TextDocument(string.Join('\n', rows.Select(r => r.Text)));
        _blameMargin.SetRows(rows);
    }

    private void ApplyBlameTheme()
    {
        if (Application.Current is not { } app) return;
        var theme = BlameEditor.ActualThemeVariant;
        if (app.TryFindResource("CanvasBrush", theme, out var bg) && bg is IBrush bgBrush)
            BlameEditor.Background = bgBrush;
        if (app.TryFindResource("TextBrush", theme, out var fg) && fg is IBrush fgBrush)
            BlameEditor.Foreground = fgBrush;
        if (app.TryFindResource("MutedBrush", theme, out var ln) && ln is IBrush lnBrush)
            BlameEditor.LineNumbersForeground = lnBrush;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape) Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Infinite scroll: the next page loads before the end of the list comes into view.</summary>
    private void OnHistoryScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is ScrollViewer sv && DataContext is FileHistoryViewModel vm && sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 400)
            _ = vm.LoadMoreAsync();
    }
}
