using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using GitGraph.App.Services;
using GitGraph.Core.Diff;

namespace GitGraph.App.Controls;

/// <summary>
/// A diff line's text, coloured by <see cref="Syntax"/>. When the line has word-level segments, the changed
/// words get a stronger added or deleted background than the line itself. Its text can be selected and copied.
/// </summary>
public sealed class DiffText : SelectableTextBlock
{
    public static readonly StyledProperty<string?> LineProperty =
        AvaloniaProperty.Register<DiffText, string?>(nameof(Line));

    public static readonly StyledProperty<IReadOnlyList<DiffSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<DiffText, IReadOnlyList<DiffSegment>?>(nameof(Segments));

    public static readonly StyledProperty<bool> IsAddedProperty =
        AvaloniaProperty.Register<DiffText, bool>(nameof(IsAdded));

    public static readonly StyledProperty<SyntaxSpan[]?> SyntaxProperty =
        AvaloniaProperty.Register<DiffText, SyntaxSpan[]?>(nameof(Syntax));

    public DiffText()
    {
        ActualThemeVariantChanged += (_, _) => { if (Syntax != null) Rebuild(); };
        // The code viewer's selection colour, and selected text keeps its syntax colours, as in VS Code.
        Bind(SelectionBrushProperty, this.GetResourceObservable("EditorSelectionBrush"));
        SelectionForegroundBrush = null;
    }

    // Styled like any other SelectableTextBlock (font, size, line height of diff lines; I-beam and Copy menu).
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    /// <summary>The plain line text, shown when there are no segments. Use this instead of Text.</summary>
    public string? Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public IReadOnlyList<DiffSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public SyntaxSpan[]? Syntax
    {
        get => GetValue(SyntaxProperty);
        set => SetValue(SyntaxProperty, value);
    }

    /// <summary>Which background marks changed words: added (true) or deleted (false).</summary>
    public bool IsAdded
    {
        get => GetValue(IsAddedProperty);
        set => SetValue(IsAddedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LineProperty || change.Property == SegmentsProperty || change.Property == IsAddedProperty
            || change.Property == SyntaxProperty) Rebuild();
    }

    private void Rebuild()
    {
        var line = Line ?? "";
        var segments = Segments;
        var syntax = Syntax;
        // Segments must cover the line exactly, or their offsets can't be lined up with the syntax spans.
        if (segments != null && segments.Sum(s => s.Text.Length) != line.Length) syntax = null;
        // Text and Inlines share one store, so set exactly one of them each time a row is reused.
        if (segments is null && syntax is null)
        {
            Inlines = null;
            Text = Line;
            return;
        }

        // Cut the line wherever a syntax span or a changed-word segment starts or ends.
        var cuts = new SortedSet<int> { 0, line.Length };
        foreach (var span in syntax ?? [])
        {
            cuts.Add(Math.Min(span.Start, line.Length));
            cuts.Add(Math.Min(span.Start + span.Length, line.Length));
        }
        var changed = new List<(int Start, int End)>();
        var offset = 0;
        foreach (var s in segments ?? [])
        {
            if (s.Changed) changed.Add((offset, offset + s.Text.Length));
            offset += s.Text.Length;
            cuts.Add(offset);
        }

        var light = ActualThemeVariant == ThemeVariant.Light;
        var key = IsAdded ? "AddWordBgBrush" : "DelWordBgBrush";
        var inlines = new InlineCollection();
        var text = segments != null && syntax is null ? string.Concat(segments.Select(s => s.Text)) : line;
        var brushes = new Dictionary<Color, IBrush>();
        int? previous = null;
        foreach (var cut in cuts)
        {
            if (previous is { } start && cut > start && cut <= text.Length)
            {
                var run = new Run(text[start..cut]);
                if (syntax != null && Array.FindLast(syntax, x => x.Start <= start && start < x.Start + x.Length) is { Length: > 0 } span)
                {
                    var color = light ? span.Light : span.Dark;
                    if (!brushes.TryGetValue(color, out var brush)) brushes[color] = brush = new SolidColorBrush(color);
                    run.Foreground = brush;
                    run.FontStyle = span.Style;
                    run.FontWeight = span.Weight;
                }
                // Bound to the resource so the colour follows theme changes.
                if (changed.Exists(c => c.Start <= start && start < c.End))
                    run.Bind(Inline.BackgroundProperty, this.GetResourceObservable(key));
                inlines.Add(run);
            }
            previous = cut;
        }
        Inlines = inlines;
    }
}
