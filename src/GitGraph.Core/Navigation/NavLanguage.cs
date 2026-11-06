using System.Reflection;
using TreeSitter;

namespace GitGraph.Core.Navigation;

/// <summary>
/// A language code navigation understands: its tree-sitter grammar and the two embedded queries
/// (<c>Queries/&lt;query&gt;.definitions.scm</c> and <c>.locals.scm</c>). The grammars must also be listed in
/// TreeSitterGrammars in Directory.Build.targets, or their native library isn't shipped.
/// </summary>
public sealed class NavLanguage
{
    public static readonly NavLanguage CSharp = new("csharp", "c-sharp", "csharp", ".cs");
    public static readonly NavLanguage Go = new("go", "go", "go", ".go");
    public static readonly NavLanguage Rust = new("rust", "rust", "rust", ".rs") { ShadowsInScope = true };
    public static readonly NavLanguage Java = new("java", "java", "java", ".java");
    public static readonly NavLanguage Python = new("python", "python", "python", ".py", ".pyi") { DeclareBeforeUse = false };
    public static readonly NavLanguage JavaScript = new("javascript", "javascript", "javascript", ".js", ".mjs", ".cjs", ".jsx");
    public static readonly NavLanguage TypeScript = new("typescript", "typescript", "typescript", ".ts", ".mts", ".cts");
    public static readonly NavLanguage Tsx = new("tsx", "tsx", "typescript", ".tsx");

    public static IReadOnlyList<NavLanguage> All { get; } = [CSharp, Go, Rust, Java, Python, JavaScript, TypeScript, Tsx];

    private static readonly Dictionary<string, NavLanguage> ByExtension =
        All.SelectMany(l => l.Extensions.Select(e => (e, l))).ToDictionary(x => x.e, x => x.l, StringComparer.OrdinalIgnoreCase);

    private readonly Lazy<Language> _grammar;

    private NavLanguage(string id, string grammar, string queries, params string[] extensions)
    {
        Id = id;
        Extensions = extensions;
        QueryName = queries;
        // The library and entry point are named explicitly: Language(id) builds the file name from the id, and
        // file names are case-sensitive on Linux.
        var symbol = "tree_sitter_" + grammar.Replace('-', '_');
        _grammar = new Lazy<Language>(() => new Language("tree-sitter-" + grammar, symbol));
    }

    /// <summary>Stable name, stored in the index.</summary>
    public string Id { get; }
    public IReadOnlyList<string> Extensions { get; }
    internal string QueryName { get; }

    /// <summary>A declaration may shadow an earlier one in the same scope (Rust's <c>let</c>).</summary>
    internal bool ShadowsInScope { get; private init; }

    /// <summary>
    /// A local is used only after its declaration. Not so in Python, where a comprehension's body comes before its
    /// <c>for</c>.
    /// </summary>
    internal bool DeclareBeforeUse { get; private init; } = true;

    /// <summary>The grammar, loaded on first use (loading the native library takes a few hundred ms). Thread-safe.</summary>
    internal Language Grammar => _grammar.Value;

    public static NavLanguage? ForPath(string path) => ByExtension.GetValueOrDefault(Path.GetExtension(path));

    public static NavLanguage? ById(string id) => All.FirstOrDefault(l => l.Id == id);

    internal string LoadQuery(string kind)
    {
        var name = $"GitGraph.Core.Navigation.Queries.{QueryName}.{kind}.scm";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded query {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>A hash of every query, so an index built with older queries is rebuilt.</summary>
    public static string QueriesHash()
    {
        var queries = All.DistinctBy(l => l.QueryName).SelectMany(l => new[] { l.LoadQuery("definitions"), l.LoadQuery("locals") });
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", queries));
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
    }

    public override string ToString() => Id;
}
