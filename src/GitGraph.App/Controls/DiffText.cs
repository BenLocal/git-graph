using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using GitGraph.Core.Diff;

namespace GitGraph.App.Controls;

/// <summary>
/// A diff line's text. When the line has word-level segments, the changed words get a stronger
/// added or deleted background than the line itself.
/// </summary>
public sealed class DiffText : TextBlock
{
    public static readonly StyledProperty<string?> LineProperty =
        AvaloniaProperty.Register<DiffText, string?>(nameof(Line));

    public static readonly StyledProperty<IReadOnlyList<DiffSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<DiffText, IReadOnlyList<DiffSegment>?>(nameof(Segments));

    public static readonly StyledProperty<bool> IsAddedProperty =
        AvaloniaProperty.Register<DiffText, bool>(nameof(IsAdded));

    // Styled like any other TextBlock (font, size, line height of diff lines).
    protected override Type StyleKeyOverride => typeof(TextBlock);

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

    /// <summary>Which background marks changed words: added (true) or deleted (false).</summary>
    public bool IsAdded
    {
        get => GetValue(IsAddedProperty);
        set => SetValue(IsAddedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LineProperty || change.Property == SegmentsProperty || change.Property == IsAddedProperty) Rebuild();
    }

    private void Rebuild()
    {
        var segments = Segments;
        // Text and Inlines share one store, so set exactly one of them each time a row is reused.
        if (segments is null)
        {
            Inlines = null;
            Text = Line;
            return;
        }
        var inlines = new InlineCollection();
        var key = IsAdded ? "AddWordBgBrush" : "DelWordBgBrush";
        foreach (var s in segments)
        {
            var run = new Run(s.Text);
            // Bound to the resource so the colour follows theme changes.
            if (s.Changed) run.Bind(Inline.BackgroundProperty, this.GetResourceObservable(key));
            inlines.Add(run);
        }
        Inlines = inlines;
    }
}
