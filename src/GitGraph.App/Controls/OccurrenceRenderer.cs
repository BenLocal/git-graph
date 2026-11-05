using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using GitGraph.Core.Navigation;

namespace GitGraph.App.Controls;

/// <summary>Washes every occurrence of the name at the caret, as VS Code highlights a symbol's uses.</summary>
public sealed class OccurrenceRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private IReadOnlyList<TextSpan> _spans = [];

    public OccurrenceRenderer(TextEditor editor)
    {
        _editor = editor;
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
    }

    public IReadOnlyList<TextSpan> Spans
    {
        get => _spans;
        set
        {
            if (_spans.Count == 0 && value.Count == 0) return;
            _spans = value;
            _editor.TextArea.TextView.InvalidateLayer(Layer);
        }
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (_spans.Count == 0 || !textView.VisualLinesValid || textView.Document is not { } doc) return;
        var brush = _editor.Brush("HoverWashBrush");
        foreach (var span in _spans)
        {
            if (span.End > doc.TextLength) continue;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
            builder.AddSegment(textView, new TextSegment { StartOffset = span.Start, EndOffset = span.End });
            if (builder.CreateGeometry() is { } geometry) dc.DrawGeometry(brush, null, geometry);
        }
    }
}
