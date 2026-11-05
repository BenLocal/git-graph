using GitGraph.Core.Navigation;

namespace GitGraph.Core.Tests;

public sealed class SourceParserTests : IDisposable
{
    private readonly SourceParser _parser = new();

    public void Dispose() => _parser.Dispose();

    private ParsedSource Parse(string path, string text) => _parser.Parse(path, text)!;

    private static string Defs(ParsedSource src) =>
        string.Join("\n", src.Definitions().Select(d => $"{d.Kind} {d.Name}{(d.Container is { } c ? " in " + c : "")} @{d.Span.Line + 1}"));

    private static string Supers(ParsedSource src) =>
        string.Join("\n", src.Supers().Select(s => $"{s.TypeName} : {s.SuperName}"));

    /// <summary>The lines (1-based) of every use of the local found at the n-th occurrence of <paramref name="marker"/>.</summary>
    private static int[] LocalUses(ParsedSource src, string marker, int occurrence = 0)
    {
        var at = -1;
        for (var i = 0; i <= occurrence; i++) at = src.Text.IndexOf(marker, at + 1, StringComparison.Ordinal);
        Assert.True(at >= 0, $"marker {marker} not found");
        var local = src.ResolveLocal(at);
        Assert.True(local != null, $"no local at {marker}");
        return local.Uses.Select(u => u.Line + 1).ToArray();
    }

    [Fact]
    public void Languages_are_found_by_extension()
    {
        Assert.Equal(NavLanguage.CSharp, NavLanguage.ForPath("src/A.CS"));
        Assert.Equal(NavLanguage.Tsx, NavLanguage.ForPath("ui/App.tsx"));
        Assert.Equal(NavLanguage.JavaScript, NavLanguage.ForPath("a.mjs"));
        Assert.Null(NavLanguage.ForPath("README.md"));
        Assert.Null(_parser.Parse("notes.txt", "hello"));
    }

    [Fact]
    public void Every_language_loads_its_grammar_and_queries()
    {
        foreach (var language in NavLanguage.All)
        {
            using var src = _parser.Parse(language, "");
            Assert.Empty(src.Definitions());
        }
    }

    [Fact]
    public void Simple_names_drop_paths_generics_and_pointers()
    {
        Assert.Equal("IList", ParsedSource.SimpleName("System.Collections.Generic.IList<T>"));
        Assert.Equal("Server", ParsedSource.SimpleName("*pkg.Server"));
        Assert.Equal("Display", ParsedSource.SimpleName("std::fmt::Display"));
        Assert.Equal("Base", ParsedSource.SimpleName("Base"));
    }

    private const string CSharpSource = """
        namespace Demo;

        public interface IShape { double Area(); }

        public sealed class Circle : Shape, IShape, IEquatable<Circle>
        {
            private readonly double _radius, _unused;
            public event EventHandler? Changed;
            public double Radius => _radius;
            public Circle(double radius) { _radius = radius; }
            public double Area() => Math.PI * _radius * _radius; // Area in a comment
            public bool Equals(Circle? other) => other is Circle node && node._radius == _radius;
        }

        public enum Colour { Red, Green }

        public record Point(int X, int Y);

        static class Tools
        {
            public static int Sum(int[] items)
            {
                var total = 0;
                foreach (var item in items) total += item;
                Func<int, int> twice = item => item * 2;
                int Local(int total) => total + 1;
                return Local(total) + twice(total);
            }
        }
        """;

    [Fact]
    public void CSharp_definitions_and_containers()
    {
        using var src = Parse("Shapes.cs", CSharpSource);
        Assert.Equal("""
            Interface IShape @3
            Method Area in IShape @3
            Class Circle @5
            Field _radius in Circle @7
            Field _unused in Circle @7
            Field Changed in Circle @8
            Property Radius in Circle @9
            Constructor Circle in Circle @10
            Method Area in Circle @11
            Method Equals in Circle @12
            Enum Colour @15
            Constant Red in Colour @15
            Constant Green in Colour @15
            Class Point @17
            Class Tools @19
            Method Sum in Tools @21
            """, Defs(src));
        Assert.Equal("""
            Circle : Shape
            Circle : IShape
            Circle : IEquatable
            """, Supers(src));
    }

    [Fact]
    public void CSharp_identifiers_skip_comments_and_strings()
    {
        using var src = Parse("Shapes.cs", CSharpSource);
        Assert.Equal(2, src.Identifiers().Count(i => i.Name == "Area"));
        var radius = src.Identifiers().Where(i => i.Name == "_radius").ToList();
        // `node._radius` is a member access; the others are plain names.
        Assert.Single(radius, i => i.IsMember);
    }

    [Fact]
    public void CSharp_locals_resolve_by_scope()
    {
        using var src = Parse("Shapes.cs", CSharpSource);
        // `total` the variable vs `total` the local function's parameter.
        Assert.Equal([23, 24, 27, 27], LocalUses(src, "total = 0"));
        Assert.Equal([26, 26], LocalUses(src, "total) =>"));
        // The foreach variable and the lambda parameter share a name but not a scope.
        Assert.Equal([24, 24], LocalUses(src, "item in"));
        Assert.Equal([25, 25], LocalUses(src, "item =>"));
        // A pattern variable, and a constructor parameter.
        Assert.Equal([12, 12], LocalUses(src, "node &&"));
        Assert.Equal([10, 10], LocalUses(src, "radius)"));
        // Local functions are locals of the enclosing scope.
        Assert.Equal([26, 27], LocalUses(src, "Local(int"));
    }

    [Fact]
    public void CSharp_members_and_types_are_not_locals()
    {
        using var src = Parse("Shapes.cs", CSharpSource);
        Assert.Null(src.ResolveLocal(src.Text.IndexOf("_radius;", StringComparison.Ordinal)));
        Assert.Null(src.ResolveLocal(src.Text.IndexOf("Circle? other", StringComparison.Ordinal)));
        Assert.Null(src.ResolveLocal(src.Text.IndexOf("Area in a comment", StringComparison.Ordinal)));
        Assert.Equal("Radius", src.IdentifierAt(src.Text.IndexOf("Radius =>", StringComparison.Ordinal) + 6)?.Name);
    }

    [Fact]
    public void Go_definitions_and_locals()
    {
        using var src = Parse("shapes.go", """
            package shapes

            type Shape interface { Area() float64 }
            type Circle struct { Radius float64 }
            type ID = string
            const Pi = 3.14
            var Count int
            func (c *Circle) Area() float64 { return Pi * c.Radius }
            func Sum(items []int, rest ...int) int {
            	total := 0
            	for _, item := range items { total += item }
            	f := func(item int) int { return item * 2 }
            	if v, ok := m[1]; ok { total += v }
            	switch x := any(total).(type) { case int: _ = x }
            	return total + f(total)
            }
            """);
        Assert.Equal("""
            Interface Shape @3
            Method Area in Shape @3
            Struct Circle @4
            Field Radius in Circle @4
            Type ID @5
            Constant Pi @6
            Variable Count @7
            Method Area in Circle @8
            Function Sum @9
            """, Defs(src));
        Assert.Empty(src.Supers());
        Assert.Equal([10, 11, 13, 14, 15, 15], LocalUses(src, "total :="));
        Assert.Equal([11, 11], LocalUses(src, "item :="));
        Assert.Equal([12, 12], LocalUses(src, "item int"));
        Assert.Equal([13, 13], LocalUses(src, "v, ok"));
        Assert.Equal([14, 14], LocalUses(src, "x :="));
        Assert.Equal([8, 8], LocalUses(src, "c *Circle"));
        // `c.Radius`: the field after the dot is not the receiver.
        Assert.Null(src.ResolveLocal(src.Text.IndexOf("Radius }", StringComparison.Ordinal)));
    }

    [Fact]
    public void Rust_definitions_and_locals()
    {
        using var src = Parse("lib.rs", """
            pub trait Shape: Display + fmt::Debug { fn area(&self) -> f64; }
            pub struct Circle { radius: f64 }
            pub enum Colour { Red, Green(u8) }
            type Id = String;
            const PI: f64 = 3.14;
            static COUNT: i32 = 0;
            mod inner {}
            macro_rules! twice { ($x:expr) => { $x * 2 } }
            impl Shape for Circle { fn area(&self) -> f64 { PI * self.radius } }
            impl<T> Circle { pub fn new(radius: f64) -> Self { Circle { radius } } }
            fn sum(items: &[i32]) -> i32 {
                let total = 0;
                let total = total + 1;
                for item in items { let _ = item; }
                let f = |item: i32| item * 2;
                let (a, b) = (1, 2);
                if let Some(v) = items.first() { let _ = v; }
                total + f(total) + a + b
            }
            """);
        Assert.Equal("""
            Interface Shape @1
            Method area in Shape @1
            Struct Circle @2
            Field radius in Circle @2
            Enum Colour @3
            Constant Red in Colour @3
            Constant Green in Colour @3
            Type Id @4
            Constant PI @5
            Constant COUNT @6
            Namespace inner @7
            Function twice @8
            Method area in Circle @9
            Method new in Circle @10
            Function sum @11
            """, Defs(src));
        Assert.Equal("""
            Shape : Display
            Shape : Debug
            Circle : Shape
            """, Supers(src));
        // Shadowing: the second `total` reads the first one on its right-hand side.
        Assert.Equal([12, 13], LocalUses(src, "total = 0"));
        Assert.Equal([13, 18, 18], LocalUses(src, "total = total"));
        Assert.Equal([14, 14], LocalUses(src, "item in"));
        Assert.Equal([15, 15], LocalUses(src, "item: i32"));
        Assert.Equal([16, 18], LocalUses(src, "a, b"));
        Assert.Equal([17, 17], LocalUses(src, "v)"));
        Assert.Equal([10, 10], LocalUses(src, "radius: f64)"));
    }

    [Fact]
    public void Python_definitions_and_locals()
    {
        using var src = Parse("shapes.py", """
            import os
            from typing import List as L

            LIMIT = 10

            class Shape(Base, metaclass=ABCMeta):
                sides = 0
                def area(self, scale=1, *args, **kw):
                    return self.sides * scale

            def total(items: list, start: int = 0):
                result = start
                for item in items:
                    result += item
                twice = lambda item: item * 2
                squares = [item * item for item in items]
                with open("f") as fh:
                    result = result + len(fh.read())
                try:
                    pass
                except Exception as err:
                    print(err)
                return twice(result) + LIMIT
            """);
        Assert.Equal("""
            Variable LIMIT @4
            Class Shape @6
            Field sides in Shape @7
            Method area in Shape @8
            Function total @11
            """, Defs(src));
        Assert.Equal("Shape : Base", Supers(src));
        // Every assignment in a function is the same variable.
        Assert.Equal([12, 14, 18, 18, 23], LocalUses(src, "result = start"));
        Assert.Equal([13, 14], LocalUses(src, "item in items:"));
        Assert.Equal([15, 15], LocalUses(src, "item: item"));
        Assert.Equal([16, 16, 16], LocalUses(src, "item * item"));
        Assert.Equal([17, 18], LocalUses(src, "fh:"));
        Assert.Equal([21, 22], LocalUses(src, "err:"));
        Assert.Equal([4, 23], LocalUses(src, "LIMIT = 10"));
        Assert.Equal([8, 9], LocalUses(src, "scale=1"));
        Assert.Null(src.ResolveLocal(src.Text.IndexOf("sides *", StringComparison.Ordinal)));
    }

    [Fact]
    public void JavaScript_definitions_and_locals()
    {
        using var src = Parse("shapes.js", """
            import fs, { readFile as rf } from "fs";
            export class Circle extends Shape {
              radius = 1;
              area() { return Math.PI * this.radius; }
            }
            function sum(items, start = 0, ...rest) {
              let total = start;
              for (const item of items) total += item;
              const twice = item => item * 2;
              const { a, b: c } = obj;
              try {} catch (err) { console.log(err); }
              return twice(total) + a + c;
            }
            export const helper = (x) => x + 1;
            const LIMIT = 3;
            """);
        Assert.Equal("""
            Class Circle @2
            Field radius in Circle @3
            Method area in Circle @4
            Function sum @6
            Function helper @14
            Variable LIMIT @15
            """, Defs(src));
        Assert.Equal("Circle : Shape", Supers(src));
        Assert.Equal([7, 8, 12], LocalUses(src, "total = start"));
        Assert.Equal([6, 7], LocalUses(src, "start = 0"));
        Assert.Equal([8, 8], LocalUses(src, "item of"));
        Assert.Equal([9, 9], LocalUses(src, "item =>"));
        Assert.Equal([10, 12], LocalUses(src, "a, b"));
        Assert.Equal([10, 12], LocalUses(src, "c }"));
        Assert.Equal([11, 11], LocalUses(src, "err)"));
        Assert.Null(src.ResolveLocal(src.Text.IndexOf("radius;", StringComparison.Ordinal)));
    }

    [Fact]
    public void TypeScript_definitions_and_locals()
    {
        const string source = """
            export interface Shape extends Base, Other<T> { area(): number; name: string; }
            export abstract class Circle extends Base<number> implements Shape, Comparable {
              private radius: number = 1;
              abstract scale(): void;
              area(): number { return this.radius; }
            }
            type Id = string;
            enum Colour { Red, Green }
            namespace NS {}
            export function sum(items: number[], start?: number): number {
              let total = start ?? 0;
              for (const item of items) total += item;
              const twice = (item: number) => item * 2;
              return twice(total);
            }
            declare function external(a: string): void;
            """;
        foreach (var path in new[] { "shapes.ts", "shapes.tsx" })
        {
            using var src = Parse(path, source);
            Assert.Equal("""
                Interface Shape @1
                Method area in Shape @1
                Property name in Shape @1
                Class Circle @2
                Field radius in Circle @3
                Method scale in Circle @4
                Method area in Circle @5
                Type Id @7
                Enum Colour @8
                Namespace NS @9
                Function sum @10
                Function external @16
                """, Defs(src));
            Assert.Equal("""
                Shape : Base
                Shape : Other
                Circle : Base
                Circle : Shape
                Circle : Comparable
                """, Supers(src));
            Assert.Equal([11, 12, 14], LocalUses(src, "total ="));
            Assert.Equal([10, 11], LocalUses(src, "start?"));
            Assert.Equal([12, 12], LocalUses(src, "item of"));
            Assert.Equal([13, 13], LocalUses(src, "item: number)"));
        }
    }
}
