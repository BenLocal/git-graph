using System.Globalization;

namespace GitGraph.Core.Stats;

public sealed record LanguageSummary(string Name, long Bytes, double Share);

/// <summary>Offline estimates from committed blob sizes, using common filenames and extensions.</summary>
public static class LanguageStats
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".fs"] = "F#",
        [".fsx"] = "F#",
        [".vb"] = "Visual Basic .NET",
        [".c"] = "C",
        [".h"] = "C",
        [".cpp"] = "C++",
        [".cc"] = "C++",
        [".cxx"] = "C++",
        [".hpp"] = "C++",
        [".rs"] = "Rust",
        [".go"] = "Go",
        [".java"] = "Java",
        [".kt"] = "Kotlin",
        [".kts"] = "Kotlin",
        [".py"] = "Python",
        [".pyw"] = "Python",
        [".rb"] = "Ruby",
        [".php"] = "PHP",
        [".js"] = "JavaScript",
        [".jsx"] = "JavaScript",
        [".mjs"] = "JavaScript",
        [".cjs"] = "JavaScript",
        [".ts"] = "TypeScript",
        [".tsx"] = "TypeScript",
        [".mts"] = "TypeScript",
        [".cts"] = "TypeScript",
        [".html"] = "HTML",
        [".htm"] = "HTML",
        [".css"] = "CSS",
        [".scss"] = "SCSS",
        [".sass"] = "Sass",
        [".less"] = "Less",
        [".vue"] = "Vue",
        [".svelte"] = "Svelte",
        [".xml"] = "XML",
        [".axaml"] = "XML",
        [".xaml"] = "XML",
        [".razor"] = "Razor",
        [".cshtml"] = "Razor",
        [".sh"] = "Shell",
        [".bash"] = "Shell",
        [".zsh"] = "Shell",
        [".ps1"] = "PowerShell",
        [".psm1"] = "PowerShell",
        [".bat"] = "Batchfile",
        [".cmd"] = "Batchfile",
        [".sql"] = "SQL",
        [".lua"] = "Lua",
        [".swift"] = "Swift",
        [".dart"] = "Dart",
        [".scala"] = "Scala",
        [".sc"] = "Scala",
        [".r"] = "R",
        [".jl"] = "Julia",
        [".pl"] = "Perl",
        [".pm"] = "Perl",
        [".ex"] = "Elixir",
        [".exs"] = "Elixir",
        [".erl"] = "Erlang",
        [".hs"] = "Haskell",
        [".clj"] = "Clojure",
        [".zig"] = "Zig",
        [".cmake"] = "CMake",
        [".mk"] = "Makefile",
        [".proto"] = "Protocol Buffer",
        [".tf"] = "HCL",
        [".v"] = "Verilog",
        [".sv"] = "SystemVerilog",
        [".vhd"] = "VHDL",
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "vendor", "vendors", "third_party", "third-party", "packages", "bower_components",
        "bin", "obj", "dist", "build", "target", ".venv", "venv", "__pycache__", "generated",
        "docs", "doc", "documentation",
    };

    public static IReadOnlyList<LanguageSummary> ParseTree(string output, CancellationToken ct = default)
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            var tab = entry.IndexOf('\t');
            if (tab < 0) continue;
            var metadata = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 4 || metadata[0] is not ("100644" or "100755") || metadata[1] != "blob" ||
                !long.TryParse(metadata[3], NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size == 0) continue;
            var path = entry[(tab + 1)..];
            var parts = path.Split('/');
            if (parts.SkipLast(1).Any(ExcludedDirectories.Contains)) continue;
            var name = parts[^1];
            if (name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)) continue;
            var language = name switch
            {
                "Makefile" or "makefile" or "GNUmakefile" => "Makefile",
                "Dockerfile" => "Dockerfile",
                "CMakeLists.txt" => "CMake",
                _ when System.IO.Path.GetExtension(name) == ".C" => "C++",
                _ => Extensions.GetValueOrDefault(System.IO.Path.GetExtension(name)),
            };
            if (language != null) totals[language] = totals.GetValueOrDefault(language) + size;
        }
        var total = totals.Values.Sum();
        return totals.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new LanguageSummary(x.Key, x.Value, (double)x.Value / total)).ToList();
    }
}
