using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Folding;

namespace GitGraph.App.Controls;

/// <summary>Theme resources for controls that draw in code.</summary>
public static class ResourceBrushes
{
    /// <summary>A brush from <c>Tokens.axaml</c> in the element's current theme, or <paramref name="fallback"/> (transparent).</summary>
    public static IBrush Brush(this StyledElement element, string key, IBrush? fallback = null) =>
        element.TryFindResource(key, element.ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback ?? Brushes.Transparent;

    /// <summary>
    /// The app's look for a read-only code editor: its own canvas and line-number colours rather than the TextMate
    /// theme's, VS Code's selection colour, on which text keeps its colours (the app's amber accent hid it), and
    /// Primer's colours on the fold markers and the line-number rule.
    /// </summary>
    public static void ApplyEditorTheme(TextEditor editor)
    {
        editor.Background = editor.Brush("CanvasBrush");
        editor.LineNumbersForeground = editor.Brush("MutedBrush");
        editor.TextArea.SelectionBrush = editor.Brush("EditorSelectionBrush");
        editor.TextArea.SelectionForeground = null;
        editor.TextArea.SelectionBorder = null;
        // Fold markers: a grey box on the canvas; AvaloniaEdit draws the + / − sign in the selected brush, Primer's blue.
        editor.SetValue(FoldingMargin.FoldingMarkerBrushProperty, editor.Brush("MutedBrush"));
        editor.SetValue(FoldingMargin.FoldingMarkerBackgroundBrushProperty, editor.Brush("CanvasBrush"));
        editor.SetValue(FoldingMargin.SelectedFoldingMarkerBrushProperty, editor.Brush("SignalBrush"));
        editor.SetValue(FoldingMargin.SelectedFoldingMarkerBackgroundBrushProperty, editor.Brush("SignalWashBrush"));
        // A solid rule between the line numbers and the code instead of AvaloniaEdit's dotted line.
        foreach (var line in editor.TextArea.LeftMargins.OfType<Line>())
        {
            line.Stroke = editor.Brush("RuleBrush");
            line.StrokeDashArray = null;
        }
    }
}
