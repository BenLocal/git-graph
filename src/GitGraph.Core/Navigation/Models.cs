namespace GitGraph.Core.Navigation;

public enum SymbolKind
{
    Namespace,
    Class,
    Interface,
    Struct,
    Enum,
    Type,
    Function,
    Method,
    Constructor,
    Property,
    Field,
    Constant,
    Variable,
}

/// <summary>
/// A range of source text. Offsets and columns count UTF-16 code units, as .NET strings and AvaloniaEdit do;
/// lines and columns are 0-based.
/// </summary>
public readonly record struct TextSpan(int Start, int End, int Line, int Column)
{
    public int Length => End - Start;
    public bool Contains(int offset) => offset >= Start && offset <= End;
}

/// <summary>A definition found by a language's definitions query.</summary>
/// <param name="Container">Name of the enclosing definition (the class of a method), if any.</param>
public sealed record SymbolDef(string Name, SymbolKind Kind, TextSpan Span, string? Container);

/// <summary>A type naming a supertype it inherits or implements (<c>class A : IB</c>, <c>impl B for A</c>).</summary>
public sealed record SuperRef(string TypeName, string SuperName, TextSpan Span);

/// <summary>An identifier in the source.</summary>
/// <param name="IsMember">The name after a dot (<c>x.Name</c>), which never refers to a local.</param>
public sealed record Identifier(string Name, TextSpan Span, bool IsMember);

/// <summary>A local variable or parameter and every use of it inside its scope.</summary>
public sealed record LocalSymbol(string Name, TextSpan Declaration, IReadOnlyList<TextSpan> Uses);
