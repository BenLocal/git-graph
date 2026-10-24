using System.Text.RegularExpressions;

namespace GitGraph.App.ViewModels;

/// <summary>
/// What the find box holds, as in Sublime Merge's search: "author:ana path:src/ code:Foo since:1m tiles" filters by
/// author, path, code added or removed and period, and finds "tiles" among what is left. Values with spaces go in
/// quotes. A word with a colon that isn't one of these (fix:, http:) is plain text.
/// </summary>
public sealed partial record FindQuery(string Text, string? Author, string? Path, string? Code, string? Since, string? Problem)
{
    public bool HasFilters => Author != null || Path != null || Code != null || Since != null;

    [GeneratedRegex("""(?<=^|\s)(?<key>author|path|file|code|contents|since):(?:"(?<quoted>[^"]*)"|(?<value>\S+))""", RegexOptions.IgnoreCase)]
    private static partial Regex Filter();

    public static FindQuery Parse(string input)
    {
        string? author = null, path = null, code = null, since = null, problem = null;
        foreach (Match m in Filter().Matches(input))
        {
            var value = m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["value"].Value;
            switch (m.Groups["key"].Value.ToLowerInvariant())
            {
                case "author": author = value; break;
                case "path" or "file": path = value; break;
                case "code" or "contents": code = value; break;
                default:
                    since = Period(value);
                    if (since is null) problem = $"\"{value}\" isn't a period. Use since:week, since:month, since:3m or since:year.";
                    break;
            }
        }
        // With a mistake nothing is applied, so the text stays as typed to be corrected.
        if (problem != null) return new FindQuery(input, null, null, null, null, problem);
        var text = Regex.Replace(Filter().Replace(input, ""), @"\s+", " ").Trim();
        return new FindQuery(text, author, path, code, since, null);
    }

    /// <summary>A period as one of <see cref="RepoTabViewModel.SinceOptions"/>, or null.</summary>
    private static string? Period(string value) => value.ToLowerInvariant().Replace(".", "") switch
    {
        "week" or "1week" or "1w" or "7d" => "Last week",
        "month" or "1month" or "1m" or "30d" => "Last month",
        "3months" or "3m" or "quarter" or "90d" => "Last 3 months",
        "year" or "1year" or "1y" or "12m" or "365d" => "Last year",
        "any" or "all" or "anytime" => "Any time",
        _ => null,
    };
}
