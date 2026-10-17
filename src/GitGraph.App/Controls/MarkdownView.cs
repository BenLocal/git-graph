using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace GitGraph.App.Controls;

/// <summary>
/// Shows a commit message body as Markdown: paragraphs, headings, lists, quotes, code and links,
/// drawn with the app's own fonts and colours. Plain-text messages read the same as before.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks() // bare URLs in commit messages become links
        .UseEmphasisExtras()
        .Build();

    public MarkdownView() => Spacing = 10;

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != MarkdownProperty) return;
        Children.Clear();
        if (string.IsNullOrWhiteSpace(Markdown)) return;
        foreach (var block in Markdig.Markdown.Parse(Markdown, Pipeline))
            if (RenderBlock(block) is { } c) Children.Add(c);
    }

    private Control? RenderBlock(Block block) => block switch
    {
        HeadingBlock h => Text(h.Inline, h.Level switch { 1 => 16, 2 => 15, _ => 13.5 }, FontWeight.SemiBold),
        ParagraphBlock p => Text(p.Inline),
        ListBlock l => List(l),
        QuoteBlock q => Quote(q),
        CodeBlock c => Code(c),
        ThematicBreakBlock => Rule(),
        ContainerBlock container => Stack(container),
        _ => null,
    };

    private Border Rule()
    {
        var rule = new Border { Height = 1, Margin = new Thickness(0, 4) };
        rule.Bind(Border.BackgroundProperty, Res("RuleBrush"));
        return rule;
    }

    private StackPanel Stack(ContainerBlock container, double spacing = 8)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var child in container)
            if (RenderBlock(child) is { } c) panel.Children.Add(c);
        return panel;
    }

    private SelectableTextBlock Text(ContainerInline? inlines, double? size = null, FontWeight? weight = null)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        if (size is { } s) tb.FontSize = s;
        if (weight is { } w) tb.FontWeight = w;
        var collection = new InlineCollection();
        var links = new List<LinkSpan>();
        if (inlines != null)
            foreach (var i in inlines) AddInline(collection, i, bold: false, italic: false, links);
        tb.Inlines = collection;
        if (links.Count > 0) WireLinks(tb, links);
        return tb;
    }

    /// <summary>Where a link sits in its block's text, so a click can be matched to it.</summary>
    private sealed record LinkSpan(int Start, int End, string Url);

    /// <summary>
    /// Links are ordinary runs, so they keep the text's baseline and can be selected with it;
    /// a click is matched to a link by hit-testing the laid-out text.
    /// </summary>
    private void WireLinks(SelectableTextBlock tb, List<LinkSpan> links)
    {
        LinkSpan? At(Avalonia.Input.PointerEventArgs e)
        {
            var hit = tb.TextLayout.HitTestPoint(e.GetPosition(tb) - new Point(tb.Padding.Left, tb.Padding.Top));
            if (!hit.IsInside) return null;
            return links.FirstOrDefault(l => hit.TextPosition >= l.Start && hit.TextPosition < l.End);
        }
        tb.PointerMoved += (_, e) => tb.Cursor = At(e) is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        tb.PointerReleased += async (_, e) =>
        {
            // Leave drags alone: they select text.
            if (e.InitialPressMouseButton != MouseButton.Left || !string.IsNullOrEmpty(tb.SelectedText)) return;
            if (At(e) is not { } link || !Uri.TryCreate(link.Url, UriKind.Absolute, out var uri)) return;
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(uri);
        };
        if (links.Count == 1) ToolTip.SetTip(tb, links[0].Url);
    }

    private void AddInline(InlineCollection into, MdInline inline, bool bold, bool italic, List<LinkSpan> links)
    {
        switch (inline)
        {
            case LiteralInline lit:
                into.Add(Styled(new Run(lit.Content.ToString()), bold, italic));
                break;
            case EmphasisInline em:
                var strong = em.DelimiterCount >= 2 && em.DelimiterChar is '*' or '_';
                var strike = em.DelimiterChar == '~';
                foreach (var child in em)
                {
                    var start = into.Count;
                    AddInline(into, child, bold || strong, italic || (!strong && !strike), links);
                    if (strike)
                        for (var k = start; k < into.Count; k++)
                            if (into[k] is Run r) r.TextDecorations = TextDecorations.Strikethrough;
                }
                break;
            case CodeInline code:
            {
                var run = new Run(code.Content) { FontFamily = (FontFamily)this.FindResource("CodeFont")!, FontSize = 12 };
                run.Bind(TextElement.BackgroundProperty, Res("RaisedBrush"));
                into.Add(run);
                break;
            }
            case AutolinkInline auto:
                AddLink(into, auto.Url, auto.IsEmail ? "mailto:" + auto.Url : auto.Url, links);
                break;
            case LinkInline { IsImage: false } link:
                AddLink(into, string.Concat(link.Select(PlainText)) is { Length: > 0 } t ? t : link.Url ?? "", link.Url, links);
                break;
            case LineBreakInline br:
                into.Add(br.IsHard ? new LineBreak() : new Run(" "));
                break;
            case HtmlEntityInline entity:
                into.Add(new Run(entity.Transcoded.ToString()));
                break;
            case ContainerInline container:
                foreach (var child in container) AddInline(into, child, bold, italic, links);
                break;
            case LeafInline leaf:
                into.Add(new Run(PlainText(leaf)));
                break;
        }
    }

    private static Run Styled(Run run, bool bold, bool italic)
    {
        if (bold) run.FontWeight = FontWeight.SemiBold;
        if (italic) run.FontStyle = FontStyle.Italic;
        return run;
    }

    private void AddLink(InlineCollection into, string text, string? url, List<LinkSpan> links)
    {
        var start = into.Text?.Length ?? 0;
        var run = new Run(text) { TextDecorations = TextDecorations.Underline };
        run.Bind(TextElement.ForegroundProperty, Res("LinkBrush"));
        into.Add(run);
        if (url != null) links.Add(new LinkSpan(start, start + text.Length, url));
    }

    private static string PlainText(MdInline inline) => inline switch
    {
        LiteralInline lit => lit.Content.ToString(),
        CodeInline code => code.Content,
        ContainerInline c => string.Concat(c.Select(PlainText)),
        HtmlInline html => html.Tag,
        _ => "",
    };

    private Control List(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 4 };
        var number = int.TryParse(list.OrderedStart, out var n) ? n : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = list.IsOrdered ? $"{number++}." : "•";
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*") };
            var bullet = new TextBlock { Text = marker, LineHeight = 20, HorizontalAlignment = HorizontalAlignment.Left };
            bullet.Bind(TextBlock.ForegroundProperty, Res("MutedBrush"));
            row.Children.Add(bullet);
            var content = Stack(item, 4);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
        }
        return panel;
    }

    private Control Quote(QuoteBlock quote)
    {
        var content = Stack(quote);
        content.Bind(TextElement.ForegroundProperty, Res("MutedBrush"));
        var border = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 0, 0, 0), Child = content };
        border.Bind(Border.BorderBrushProperty, Res("RuleBrush"));
        return border;
    }

    private Control Code(CodeBlock code)
    {
        var text = string.Join('\n', code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString()));
        var tb = new SelectableTextBlock
        {
            Text = text,
            FontFamily = (FontFamily)this.FindResource("CodeFont")!,
            FontSize = 12,
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
        };
        var border = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), Child = tb };
        border.Bind(Border.BackgroundProperty, Res("RaisedBrush"));
        return border;
    }

    private IObservable<object?> Res(string key) => this.GetResourceObservable(key);
}
