using TreeSitter;

namespace GitGraph.Core.Navigation;

/// <summary>
/// Parses source files with tree-sitter. Not thread-safe: use one instance per thread. A <see cref="ParsedSource"/>
/// uses this parser's queries, so dispose it before the parser.
/// </summary>
public sealed class SourceParser : IDisposable
{
    private readonly Dictionary<NavLanguage, Entry> _entries = [];

    private sealed record Entry(Parser Parser, Query Definitions, Query Locals);

    public ParsedSource? Parse(string path, string text) =>
        NavLanguage.ForPath(path) is { } language ? Parse(language, text) : null;

    public ParsedSource Parse(NavLanguage language, string text)
    {
        if (!_entries.TryGetValue(language, out var entry))
        {
            entry = new Entry(new Parser(language.Grammar),
                new Query(language.Grammar, language.LoadQuery("definitions")),
                new Query(language.Grammar, language.LoadQuery("locals")));
            _entries[language] = entry;
        }
        var tree = entry.Parser.Parse(text) ?? throw new InvalidOperationException($"tree-sitter could not parse {language} source");
        return new ParsedSource(language, text, tree, entry.Definitions, entry.Locals);
    }

    public void Dispose()
    {
        foreach (var e in _entries.Values)
        {
            e.Definitions.Dispose();
            e.Locals.Dispose();
            e.Parser.Dispose();
        }
        _entries.Clear();
    }
}

/// <summary>One parsed file: its definitions, supertypes, identifiers and scope-aware local variables.</summary>
public sealed class ParsedSource : IDisposable
{
    private readonly Tree _tree;
    private readonly Query _definitions;
    private readonly Query _locals;
    private List<Identifier>? _identifiers;
    private Locals? _localInfo;

    internal ParsedSource(NavLanguage language, string text, Tree tree, Query definitions, Query locals)
    {
        Language = language;
        Text = text;
        _tree = tree;
        _definitions = definitions;
        _locals = locals;
    }

    public NavLanguage Language { get; }
    public string Text { get; }

    public void Dispose() => _tree.Dispose();

    private static TextSpan SpanOf(Node n) => new(n.StartIndex, n.EndIndex, n.StartPosition.Row, n.StartPosition.Column);

    /// <summary>
    /// The definitions in document order. When several patterns capture the same name, the first pattern in the
    /// query file wins, so queries list specific patterns (a method in an impl block) before general ones.
    /// </summary>
    public IReadOnlyList<SymbolDef> Definitions()
    {
        var found = new Dictionary<int, (int Pattern, string Name, SymbolKind Kind, TextSpan NameSpan, int Start, int End, string? Receiver)>();
        var containers = new List<(int Start, int End, string Name, SymbolKind? Kind)>();
        using (var cursor = _definitions.Execute(_tree.RootNode))
        {
            foreach (var match in cursor.Matches)
            {
                Node? name = null, def = null, receiver = null, container = null, containerName = null;
                SymbolKind? kind = null;
                foreach (var c in match.Captures)
                {
                    switch (c.Name)
                    {
                        case "name": name = c.Node; break;
                        case "receiver": receiver = c.Node; break;
                        case "container": container = c.Node; break;
                        case "container.name": containerName = c.Node; break;
                        default:
                            if (c.Name.StartsWith("definition.", StringComparison.Ordinal)
                                && Enum.TryParse<SymbolKind>(c.Name["definition.".Length..], true, out var k))
                            {
                                def = c.Node;
                                kind = k;
                            }
                            break;
                    }
                }
                if (container != null && containerName != null)
                    containers.Add((container.StartIndex, container.EndIndex, SimpleName(containerName.Text), null));
                if (name == null || def == null || kind == null) continue;
                if (found.TryGetValue(name.StartIndex, out var seen) && seen.Pattern <= match.PatternIndex) continue;
                found[name.StartIndex] = (match.PatternIndex, name.Text, kind.Value, SpanOf(name), def.StartIndex, def.EndIndex,
                    receiver == null ? null : SimpleName(receiver.Text));
            }
        }

        // The container of a definition is the innermost definition (or container capture) around it.
        var defs = found.Values.ToList();
        containers.AddRange(defs.Select(d => (d.Start, d.End, d.Name, (SymbolKind?)d.Kind)));
        containers.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.End.CompareTo(a.End));
        var result = new List<SymbolDef>(defs.Count);
        foreach (var d in defs.OrderBy(d => d.NameSpan.Start))
        {
            (string Name, SymbolKind? Kind)? outer = null;
            foreach (var c in containers)
            {
                if (c.Start > d.Start) break;
                if (c.End >= d.End && !(c.Start == d.Start && c.End == d.End)) outer = (c.Name, c.Kind);
            }
            var containerName = d.Receiver ?? outer?.Name;
            var kind = d.Kind;
            // A function declared inside a type (or an impl block, or with a receiver) is a method.
            if (kind == SymbolKind.Function && (d.Receiver != null || (outer is { } o && o.Kind is null or SymbolKind.Class
                    or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Enum or SymbolKind.Type)))
                kind = SymbolKind.Method;
            result.Add(new SymbolDef(d.Name, kind, d.NameSpan, containerName));
        }
        return result;
    }

    /// <summary>Types and the supertypes they name (base lists, <c>impl Trait for Type</c>, extends / implements).</summary>
    public IReadOnlyList<SuperRef> Supers()
    {
        var result = new List<SuperRef>();
        using var cursor = _definitions.Execute(_tree.RootNode);
        foreach (var match in cursor.Matches)
        {
            Node? type = null, super = null;
            foreach (var c in match.Captures)
            {
                if (c.Name == "impl.type") type = c.Node;
                else if (c.Name == "impl.super") super = c.Node;
            }
            if (type == null || super == null) continue;
            var superName = SimpleName(super.Text);
            if (superName.Length > 0) result.Add(new SuperRef(SimpleName(type.Text), superName, SpanOf(super)));
        }
        return result;
    }

    /// <summary>
    /// The bare name of a type expression: no namespace or module path, generic arguments, pointer or reference
    /// marks. <c>System.Collections.Generic.IList&lt;T&gt;</c> → <c>IList</c>, <c>*pkg.Server</c> → <c>Server</c>.
    /// </summary>
    public static string SimpleName(string text)
    {
        var s = text;
        var cut = s.IndexOfAny(['<', '[', '(', '{']);
        if (cut >= 0) s = s[..cut];
        s = s.Trim().TrimStart('*', '&').Trim();
        if (s.StartsWith("mut ", StringComparison.Ordinal)) s = s[4..].Trim();
        if (s.StartsWith("dyn ", StringComparison.Ordinal)) s = s[4..].Trim();
        // The part after the last '.' (C#, Go, Python, TS) or '::' (Rust).
        return s[(s.LastIndexOfAny(['.', ':']) + 1)..].Trim();
    }

    /// <summary>Every identifier in document order; comments and strings contain none.</summary>
    public IReadOnlyList<Identifier> Identifiers()
    {
        if (_identifiers != null) return _identifiers;
        var locals = LocalInfo();
        var list = new List<Identifier>();
        using var cursor = _tree.Walk();
        while (true)
        {
            var node = cursor.CurrentNode;
            // Leaf nodes named *identifier* (identifier, type_identifier, field_identifier, shorthand_property_identifier…),
            // and leaves the locals query declares under another name (C#'s implicit_parameter).
            if (node.IsNamed && node.FirstChild == null
                && (node.Type.Contains("identifier", StringComparison.Ordinal) || locals.DefinitionStarts.Contains(node.StartIndex)))
                list.Add(new Identifier(node.Text, SpanOf(node), locals.Members.Contains(node.StartIndex)));
            if (cursor.GotoFirstChild()) continue;
            while (!cursor.GotoNextSibling())
            {
                if (!cursor.GotoParent()) return _identifiers = list;
            }
        }
    }

    /// <summary>The identifier at a text offset; an offset just after the identifier also counts, as the caret sits there.</summary>
    public Identifier? IdentifierAt(int offset)
    {
        var ids = Identifiers();
        // Binary search for the last identifier starting at or before the offset.
        int lo = 0, hi = ids.Count - 1, best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (ids[mid].Span.Start <= offset) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best >= 0 && ids[best].Span.Contains(offset) ? ids[best] : null;
    }

    /// <summary>
    /// The local variable or parameter at an offset (its declaration or any use) and all its uses, resolved by
    /// scope; null when the identifier there isn't a local, such as a member, a type or a global.
    /// </summary>
    public LocalSymbol? ResolveLocal(int offset)
    {
        if (IdentifierAt(offset) is not { IsMember: false } id) return null;
        if (Bind(id) is not { } decl) return null;
        var uses = Identifiers()
            .Where(i => !i.IsMember && i.Name == id.Name && Bind(i) == decl)
            .Select(i => i.Span)
            .ToList();
        return new LocalSymbol(id.Name, decl.Span, uses);
    }

    private sealed record Scope(int Start, int End, int NameStart);
    /// <param name="VisibleFrom">Where uses may start: after the whole <c>let</c> in Rust, so <c>let x = x + 1</c> reads the old x.</param>
    private sealed record LocalDef(string Name, TextSpan Span, Scope Scope, int VisibleFrom);
    private sealed record Locals(List<Scope> Scopes, List<LocalDef> Definitions, HashSet<int> DefinitionStarts, HashSet<int> Members);

    private Locals LocalInfo()
    {
        if (_localInfo != null) return _localInfo;
        var scopes = new List<Scope>();
        var defs = new List<(string Name, TextSpan Span, int VisibleFrom)>();
        var members = new HashSet<int>();
        using (var cursor = _locals.Execute(_tree.RootNode))
        {
            foreach (var c in cursor.Captures)
            {
                var n = c.Node;
                switch (c.Name)
                {
                    case "local.scope":
                        scopes.Add(new Scope(n.StartIndex, n.EndIndex, n.GetChildForField("name")?.StartIndex ?? -1));
                        break;
                    case "local.definition": defs.Add((n.Text, SpanOf(n), VisibleFrom(n))); break;
                    case "local.member": members.Add(n.StartIndex); break;
                }
            }
        }
        // Innermost first: scopes sorted by size, so the first one containing a point is the innermost.
        scopes.Sort((a, b) => (a.End - a.Start).CompareTo(b.End - b.Start));
        var localDefs = new List<LocalDef>();
        foreach (var (name, span, visibleFrom) in defs.DistinctBy(d => d.Span.Start))
        {
            // A function's name belongs to the scope around the function, not to the function itself.
            var scope = scopes.FirstOrDefault(s => s.Start <= span.Start && span.End <= s.End && s.NameStart != span.Start);
            if (scope != null) localDefs.Add(new LocalDef(name, span, scope, visibleFrom));
        }
        return _localInfo = new Locals(scopes, localDefs, defs.Select(d => d.Span.Start).ToHashSet(), members);
    }

    private int VisibleFrom(Node n)
    {
        if (!Language.ShadowsInScope) return n.StartIndex;
        // The let may wrap the name in a pattern: `let (a, b) = …`.
        for (var p = n.Parent; p != null; p = p.Parent)
        {
            if (p.Type == "let_declaration") return p.EndIndex;
            if (p.Type is "block" or "function_item" or "closure_expression") break;
        }
        return n.StartIndex;
    }

    private LocalDef? Bind(Identifier id)
    {
        var info = LocalInfo();
        // A declaration is its own variable, even where a later one shadows it.
        if (Language.ShadowsInScope && info.Definitions.Find(d => d.Span.Start == id.Span.Start) is { } own) return own;
        var innermost = true;
        foreach (var scope in info.Scopes)
        {
            if (scope.Start > id.Span.Start || id.Span.End > scope.End) continue;
            LocalDef? first = null, lastVisible = null;
            foreach (var d in info.Definitions)
            {
                if (d.Scope != scope || d.Name != id.Name) continue;
                var visible = d.VisibleFrom <= id.Span.Start;
                // In the scope the name is used in, a later declaration isn't it yet (`new C { X = 1 }; var X = 2;`);
                // from a nested scope (a closure) it may be.
                if (innermost && Language.DeclareBeforeUse && !visible) continue;
                first ??= d;
                if (visible) lastVisible = d;
            }
            innermost = false;
            // Rust's `let` may shadow a name in the same block, so the latest declaration wins there; elsewhere a
            // second assignment (Python) or redeclaration (Go's :=) is the same variable as the first.
            var hit = Language.ShadowsInScope ? lastVisible ?? first : first;
            if (hit != null) return hit;
        }
        return null;
    }
}
