using Avalonia.Media;
using GitGraph.Core.Diff;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using FontStyle = Avalonia.Media.FontStyle;

namespace GitGraph.App.Services;

/// <summary>A coloured run of a line, with its colour in the dark and the light theme.</summary>
public readonly record struct SyntaxSpan(int Start, int Length, Color Dark, Color Light, FontStyle Style, FontWeight Weight);

/// <summary>The syntax colours of both versions of a diffed file, by line number.</summary>
public sealed class DiffSyntax(IReadOnlyDictionary<int, SyntaxSpan[]> oldLines, IReadOnlyDictionary<int, SyntaxSpan[]> newLines)
{
    public static readonly DiffSyntax Empty = new(new Dictionary<int, SyntaxSpan[]>(), new Dictionary<int, SyntaxSpan[]>());

    /// <summary>The spans of a diff line: deleted lines come from the old file, the others from the new one.</summary>
    public SyntaxSpan[]? For(DiffLine? line) => line switch
    {
        { Kind: DiffLineKind.Deleted, OldLine: { } o } => oldLines.GetValueOrDefault(o),
        { Kind: DiffLineKind.Added or DiffLineKind.Context, NewLine: { } n } => newLines.GetValueOrDefault(n),
        _ => null,
    };
}

/// <summary>
/// Colours diff lines with the TextMate grammars and the Dark+ / Light+ themes the file viewer uses. Each version
/// of the file is tokenized from its first line, so strings and comments spanning several lines colour correctly.
/// </summary>
public static class SyntaxHighlighter
{
    private const int MaxLines = 20_000, MaxLineLength = 2_000;
    private static readonly TimeSpan LineTimeLimit = TimeSpan.FromMilliseconds(50);

    private static readonly Lock Gate = new();
    private static readonly RegistryOptions Options = new(ThemeName.DarkPlus);
    private static readonly Registry Registry = new(Options);
    private static readonly Theme DarkTheme = Theme.CreateFromRawTheme(Options.LoadTheme(ThemeName.DarkPlus), Options);
    private static readonly Theme LightTheme = Theme.CreateFromRawTheme(Options.LoadTheme(ThemeName.LightPlus), Options);
    private static readonly Dictionary<string, IGrammar?> Grammars = [];
    private static readonly Dictionary<string, (Color Dark, Color Light, FontStyle Style, FontWeight Weight)?> Styles = [];

    public static DiffSyntax Highlight(string path, FileDiff diff)
    {
        if (diff.IsBinary || diff.AllRows.Count > MaxLines) return DiffSyntax.Empty;
        try
        {
            lock (Gate)
            {
                if (GrammarFor(path) is not { } grammar) return DiffSyntax.Empty;
                // AllRows holds every line of both versions in order; the text already has tabs expanded.
                var oldLines = diff.AllRows.Select(r => r.Left).OfType<DiffLine>().Where(l => l.OldLine != null);
                var newLines = diff.AllRows.Select(r => r.Right).OfType<DiffLine>().Where(l => l.NewLine != null);
                return new DiffSyntax(Tokenize(grammar, oldLines, l => l.OldLine!.Value), Tokenize(grammar, newLines, l => l.NewLine!.Value));
            }
        }
        catch (Exception)
        {
            // Colours are a nicety: a grammar that fails leaves the diff plain.
            return DiffSyntax.Empty;
        }
    }

    private static IGrammar? GrammarFor(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 0) return null;
        if (Grammars.TryGetValue(extension, out var cached)) return cached;
        var language = Options.GetLanguageByExtension(extension);
        var grammar = language is null ? null : Registry.LoadGrammar(Options.GetScopeByLanguageId(language.Id));
        Grammars[extension] = grammar;
        return grammar;
    }

    private static Dictionary<int, SyntaxSpan[]> Tokenize(IGrammar grammar, IEnumerable<DiffLine> lines, Func<DiffLine, int> number)
    {
        var result = new Dictionary<int, SyntaxSpan[]>();
        IStateStack? state = null;
        var spans = new List<SyntaxSpan>();
        foreach (var line in lines)
        {
            if (line.Text.Length > MaxLineLength) continue;
            var tokens = grammar.TokenizeLine(line.Text, state, LineTimeLimit);
            state = tokens.RuleStack;
            spans.Clear();
            foreach (var token in tokens.Tokens)
            {
                var end = Math.Min(token.EndIndex, line.Text.Length);
                if (end <= token.StartIndex || StyleOf(token.Scopes) is not { } s) continue;
                spans.Add(new SyntaxSpan(token.StartIndex, end - token.StartIndex, s.Dark, s.Light, s.Style, s.Weight));
            }
            if (spans.Count > 0) result[number(line)] = [.. spans];
        }
        return result;
    }

    /// <summary>The colours a scope stack gets; null where neither theme sets a colour, so the text keeps the app's own.</summary>
    private static (Color Dark, Color Light, FontStyle Style, FontWeight Weight)? StyleOf(List<string> scopes)
    {
        var key = string.Join(' ', scopes);
        if (Styles.TryGetValue(key, out var cached)) return cached;
        var dark = Rule(DarkTheme, scopes);
        var light = Rule(LightTheme, scopes);
        (Color, Color, FontStyle, FontWeight)? style = null;
        if (dark.Foreground is { } d && light.Foreground is { } l)
        {
            var fs = dark.FontStyle;
            style = (d, l, (fs & 1) != 0 ? FontStyle.Italic : FontStyle.Normal, (fs & 2) != 0 ? FontWeight.Bold : FontWeight.Normal);
        }
        Styles[key] = style;
        return style;
    }

    private static (Color? Foreground, int FontStyle) Rule(Theme theme, List<string> scopes)
    {
        Color? foreground = null;
        var fontStyle = -1;
        foreach (var rule in theme.Match(scopes))
        {
            if (foreground is null && rule.foreground > 0 && Color.TryParse(theme.GetColor(rule.foreground), out var c)) foreground = c;
            if (fontStyle < 0 && (int)rule.fontStyle >= 0) fontStyle = (int)rule.fontStyle;
        }
        return (foreground, Math.Max(0, fontStyle));
    }
}
