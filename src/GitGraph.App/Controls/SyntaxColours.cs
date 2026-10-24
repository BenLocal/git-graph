using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using GitGraph.App.Services;

namespace GitGraph.App.Controls;

/// <summary>
/// Colours a read-only editor's lines with spans from <see cref="SyntaxHighlighter"/>, in the dark or light colours
/// of the editor's theme. One grammar registry serves the diffs, the blame and the code viewer this way.
/// </summary>
/// <param name="spansOf">The spans of a 1-based line number, or null to leave it plain.</param>
public sealed class SyntaxColours(TextEditor editor, Func<int, SyntaxSpan[]?> spansOf) : DocumentColorizingTransformer
{
    private readonly Dictionary<Color, IBrush> _brushes = [];

    protected override void ColorizeLine(DocumentLine line)
    {
        if (spansOf(line.LineNumber) is not { } spans) return;
        var light = editor.ActualThemeVariant == ThemeVariant.Light;
        foreach (var span in spans)
        {
            var start = Math.Min(span.Start, line.Length);
            var end = Math.Min(span.Start + span.Length, line.Length);
            if (end <= start) continue;
            var color = light ? span.Light : span.Dark;
            if (!_brushes.TryGetValue(color, out var brush)) _brushes[color] = brush = new SolidColorBrush(color);
            ChangeLinePart(line.Offset + start, line.Offset + end, e =>
            {
                e.TextRunProperties.SetForegroundBrush(brush);
                if (span.Style != FontStyle.Normal || span.Weight != FontWeight.Normal)
                {
                    var face = e.TextRunProperties.Typeface;
                    e.TextRunProperties.SetTypeface(new Typeface(face.FontFamily, span.Style, span.Weight));
                }
            });
        }
    }
}
