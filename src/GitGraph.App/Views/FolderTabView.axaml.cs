using System.ComponentModel;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using GitGraph.App.Controls;
using GitGraph.App.ViewModels;
using TextMateSharp.Grammars;

namespace GitGraph.App.Views;

public partial class FolderTabView : UserControl
{
    private FolderTabViewModel? _vm;
    private readonly RegistryOptions _grammars;
    private readonly TextMate.Installation _textMate;
    private ThemeVariant? _appliedTheme;

    public FolderTabView()
    {
        InitializeComponent();
        _grammars = new RegistryOptions(ThemeName.DarkPlus);
        _textMate = Editor.InstallTextMate(_grammars);
        // Room between the line numbers and the code.
        foreach (var margin in Editor.TextArea.LeftMargins.OfType<AvaloniaEdit.Editing.LineNumberMargin>())
            margin.Margin = new Avalonia.Thickness(4, 0, 18, 0);
        // VS Code's Dark+ / Light+ follow the app's night and day themes.
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
        if (_vm?.SelectedNode is { } node)
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (_vm?.SelectedNode != node || Tree.TreeContainerFromItem(node) is not TreeViewItem row) return;
                row.IsSelected = true;
                CenterInTree(row);
                // On a slow first layout the tree may not know its full height yet; center again once it does.
                Avalonia.Threading.DispatcherTimer.RunOnce(() => CenterInTree(row), TimeSpan.FromMilliseconds(400));
            }, TimeSpan.FromMilliseconds(150));
    }

    /// <summary>Scrolls a restored selection to the middle of the tree rather than just inside its edge.</summary>
    private void CenterInTree(TreeViewItem row)
    {
        if (Tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { Content: Visual content } sv) return;
        if (row.TranslatePoint(new Point(0, 0), content) is not { } at) return;
        var target = at.Y - (sv.Viewport.Height - row.Bounds.Height) / 2;
        sv.Offset = sv.Offset.WithY(Math.Clamp(target, 0, Math.Max(0, sv.Extent.Height - sv.Viewport.Height)));
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderTabViewModel.FileText)) ShowText();
        // A newly opened file fades in, to show the content was replaced.
        if (e.PropertyName is nameof(FolderTabViewModel.FileText) or nameof(FolderTabViewModel.Image)) Motion.FadeIn(FileArea);
    }

    private void ShowText()
    {
        var text = _vm?.FileText;
        Editor.Document = new TextDocument(text ?? "");
        Editor.ScrollToHome();
        if (text is null || _vm?.FileName is not { } name) return;
        var language = _grammars.GetLanguageByExtension(System.IO.Path.GetExtension(name));
        _textMate.SetGrammar(language is null ? null : _grammars.GetScopeByLanguageId(language.Id));
    }

    private void ApplyTheme()
    {
        var variant = ActualThemeVariant;
        if (variant == _appliedTheme) return;
        _appliedTheme = variant;
        _textMate.SetTheme(_grammars.LoadTheme(variant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus));
        // Keep the app's own canvas and line-number colours rather than the TextMate theme's.
        if (this.TryFindResource("CanvasBrush", variant, out var bg) && bg is IBrush canvas) Editor.Background = canvas;
        if (this.TryFindResource("MutedBrush", variant, out var fg) && fg is IBrush muted) Editor.LineNumbersForeground = muted;
    }
}
