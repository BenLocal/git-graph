using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace GitGraph.App.Controls;

/// <summary>Faint text a little past the end of one line of an editor, like GitLens's blame of the caret's line.</summary>
public sealed class LineNoteRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private (int Line, string Text)? _note;

    public LineNoteRenderer(TextEditor editor)
    {
        _editor = editor;
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
    }

    /// <summary>The 0-based line and the text after it; null shows nothing.</summary>
    public (int Line, string Text)? Note
    {
        get => _note;
        set
        {
            if (_note == value) return;
            _note = value;
            _editor.TextArea.TextView.InvalidateLayer(Layer);
        }
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!textView.VisualLinesValid || _note is not var (line, text) || textView.GetVisualLine(line + 1) is not { } visual) return;
        // A few spaces past the end of the line's text, in the code font, muted.
        var end = visual.GetVisualPosition(visual.VisualLengthWithEndOfLineMarker, VisualYPosition.TextTop);
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(_editor.FontFamily), _editor.FontSize, _editor.Brush("MutedBrush"));
        using (dc.PushOpacity(0.8))
            dc.DrawText(formatted, new Point(end.X - textView.HorizontalOffset + 3 * formatted.Height, end.Y - textView.VerticalOffset));
    }
}
