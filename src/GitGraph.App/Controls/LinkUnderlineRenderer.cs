using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using GitGraph.Core.Navigation;

namespace GitGraph.App.Controls;

/// <summary>Underlines the name a Ctrl+click would follow, as VS Code does while Ctrl is held over it.</summary>
public sealed class LinkUnderlineRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private TextSpan? _span;

    public LinkUnderlineRenderer(TextEditor editor)
    {
        _editor = editor;
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
    }

    public TextSpan? Span
    {
        get => _span;
        set
        {
            if (_span == value) return;
            _span = value;
            _editor.TextArea.TextView.InvalidateLayer(Layer);
        }
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (_span is not { } span || !textView.VisualLinesValid || textView.Document is not { } doc || span.End > doc.TextLength) return;
        var pen = new Pen(_editor.Brush("LinkBrush"), 1);
        var segment = new TextSegment { StartOffset = span.Start, EndOffset = span.End };
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            var y = Math.Floor(rect.Bottom) - 0.5;
            dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
        }
    }
}
