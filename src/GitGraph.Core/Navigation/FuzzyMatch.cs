namespace GitGraph.Core.Navigation;

/// <summary>
/// Fuzzy matching as in VS Code's quick open: the query's characters must appear in order, ignoring case; matches at
/// the start of a word (after a separator or at a camelCase hump) and runs of consecutive characters score higher.
/// </summary>
public static class FuzzyMatch
{
    private const int CharScore = 1;
    private const int SameCaseBonus = 1;
    private const int ConsecutiveBonus = 5;
    private const int WordStartBonus = 8;
    private const int PrefixBonus = 50;
    private const int ExactBonus = 100;

    // A match in a path's file name beats any match spread over its folders.
    private const int FileNameBonus = 1000;

    /// <summary>How well <paramref name="query"/> matches <paramref name="text"/>; null when it doesn't. An empty query matches with 0.</summary>
    public static int? Score(string query, string text)
    {
        if (query.Length == 0) return 0;
        int? best = null;
        // The first character can match in several places; keep the best run that follows from any of them.
        for (var start = 0; start <= text.Length - query.Length; start++)
        {
            if (!Same(text[start], query[0])) continue;
            if (ScoreFrom(query, text, start) is { } s && (best is null || s > best)) best = s;
        }
        if (best is { } b)
        {
            if (text.Equals(query, StringComparison.OrdinalIgnoreCase)) b += ExactBonus;
            else if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) b += PrefixBonus;
            best = b;
        }
        return best;
    }

    /// <summary>
    /// Matches a '/'-separated path: against its file name first, then (for a query that names folders too, or when the
    /// name alone doesn't match) against the whole path.
    /// </summary>
    public static int? ScorePath(string query, string path)
    {
        if (!query.Contains('/') && Score(query, path[(path.LastIndexOf('/') + 1)..]) is { } name) return name + FileNameBonus;
        return Score(query, path);
    }

    private static int? ScoreFrom(string query, string text, int start)
    {
        int score = 0, q = 0, previous = -2;
        for (var t = start; t < text.Length && q < query.Length; t++)
        {
            if (!Same(text[t], query[q])) continue;
            score += CharScore;
            if (text[t] == query[q]) score += SameCaseBonus;
            if (t == previous + 1) score += ConsecutiveBonus;
            if (IsWordStart(text, t)) score += WordStartBonus;
            previous = t;
            q++;
        }
        return q == query.Length ? score : null;
    }

    private static bool Same(char a, char b) => char.ToLowerInvariant(a) == char.ToLowerInvariant(b);

    private static bool IsWordStart(string text, int i)
    {
        if (i == 0) return true;
        var before = text[i - 1];
        return before is '/' or '\\' or '_' or '-' or '.' or ' ' || (char.IsUpper(text[i]) && char.IsLower(before));
    }
}
