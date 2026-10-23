using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;

namespace GitGraph.App.Controls;

/// <summary>Theme resources for controls that draw in code.</summary>
public static class ResourceBrushes
{
    /// <summary>A brush from <c>Tokens.axaml</c> in the element's current theme, or <paramref name="fallback"/> (transparent).</summary>
    public static IBrush Brush(this StyledElement element, string key, IBrush? fallback = null) =>
        element.TryFindResource(key, element.ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback ?? Brushes.Transparent;

    /// <summary>
    /// The app's look for a read-only code editor: its own canvas and line-number colours rather than the TextMate
    /// theme's, and VS Code's selection colour, on which text keeps its colours (the app's amber accent hid it).
    /// </summary>
    public static void ApplyEditorTheme(TextEditor editor)
    {
        editor.Background = editor.Brush("CanvasBrush");
        editor.LineNumbersForeground = editor.Brush("MutedBrush");
        editor.TextArea.SelectionBrush = editor.Brush("EditorSelectionBrush");
        editor.TextArea.SelectionForeground = null;
        editor.TextArea.SelectionBorder = null;
    }
}
