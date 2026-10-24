using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using GitGraph.App.Controls;
using GitGraph.App.ViewModels;

namespace GitGraph.App.Views;

public partial class FolderTabView : UserControl
{
    private FolderTabViewModel? _vm;

    public FolderTabView()
    {
        InitializeComponent();
        // The view model's colours, from the same TextMate grammars (VS Code's Dark+ / Light+) as the diffs.
        Editor.TextArea.TextView.LineTransformers.Add(new SyntaxColours(Editor, n => _vm?.FileSyntax?.GetValueOrDefault(n)));
        // Room between the line numbers and the code.
        foreach (var margin in Editor.TextArea.LeftMargins.OfType<AvaloniaEdit.Editing.LineNumberMargin>())
            margin.Margin = new Avalonia.Thickness(4, 0, 18, 0);
        // Dark+ or Light+ colours follow the app's night and day themes.
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        // Each folder remembers how wide its tree was dragged.
        TreeSplitter.DragCompleted += (_, _) => _vm?.SaveTreeWidth(Columns.ColumnDefinitions[0].ActualWidth);
    }

    /// <summary>Opens the editor's search bar, for the Find shortcut while a folder tab is showing.</summary>
    public void OpenSearch()
    {
        if (!Editor.IsVisible) return;
        Editor.Focus();
        Editor.SearchPanel?.Open();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as FolderTabViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        ShowText();
        if (_vm != null) Columns.ColumnDefinitions[0].Width = new Avalonia.Controls.GridLength(_vm.TreeWidth);
        // The tree resets each new row's selection (and the binding copies that back), and its SelectedItem
        // doesn't reach nested rows; so once the rows exist, select the remembered file's row directly.
        Tree.LayoutUpdated -= RestoreSelection;
        _restoring = _vm?.SelectedNode;
        _restoredY = null;
        if (_restoring != null) Tree.LayoutUpdated += RestoreSelection;
    }

    // The remembered file being selected, and where its row was last scrolled to; the restore is done once a layout
    // pass leaves the row there, or as soon as another file is picked.
    private FileNodeViewModel? _restoring;
    private double? _restoredY;

    /// <summary>
    /// After each layout pass until the tree settles: selects the remembered file's row once it exists, and scrolls it
    /// to the middle (the tree may not know its full height on the first passes, so the middle moves until then).
    /// </summary>
    private void RestoreSelection(object? sender, EventArgs e)
    {
        if (_restoring is not { } node || _vm?.SelectedNode != node)
        {
            Tree.LayoutUpdated -= RestoreSelection;
            return;
        }
        if (Tree.TreeContainerFromItem(node) is not TreeViewItem row) return;
        row.IsSelected = true;
        var y = CenterInTree(row);
        if (y is null || y == _restoredY) Tree.LayoutUpdated -= RestoreSelection;
        _restoredY = y;
    }

    /// <summary>Scrolls a restored selection to the middle of the tree rather than just inside its edge.</summary>
    /// <returns>The offset scrolled to, or null when the row can't be placed.</returns>
    private double? CenterInTree(TreeViewItem row)
    {
        if (Tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { Content: Visual content } sv) return null;
        if (row.TranslatePoint(new Point(0, 0), content) is not { } at) return null;
        var target = Math.Clamp(at.Y - (sv.Viewport.Height - row.Bounds.Height) / 2, 0, Math.Max(0, sv.Extent.Height - sv.Viewport.Height));
        sv.Offset = sv.Offset.WithY(target);
        return target;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderTabViewModel.FileText)) ShowText();
        if (e.PropertyName == nameof(FolderTabViewModel.FileSyntax)) Editor.TextArea.TextView.Redraw();
        // A newly opened file fades in, to show the content was replaced.
        if (e.PropertyName is nameof(FolderTabViewModel.FileText) or nameof(FolderTabViewModel.Image)) Motion.FadeIn(FileArea);
    }

    private void ShowText()
    {
        Editor.Document = new TextDocument(_vm?.FileText ?? "");
        Editor.ScrollToHome();
    }

    private void ApplyTheme()
    {
        ResourceBrushes.ApplyEditorTheme(Editor);
        Editor.TextArea.TextView.Redraw();
    }
}
