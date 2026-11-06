using System.Globalization;
using System.Text;

namespace GitGraph.Core.Stats;

public sealed record LanguageSummary(string Name, long Bytes, double Share);

public sealed record LanguageFile(string Hash, string Language, long Bytes);

/// <summary>Offline estimates from committed blob sizes, using common filenames and extensions.</summary>
public static class LanguageStats
{
    // git's own heuristic: a NUL byte in the first 8000 bytes makes a blob binary.
    private const int BinarySniffLength = 8000;

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

    /// <summary>Files from <c>git ls-tree -r -l -z</c> output that count towards a language, before the binary check.</summary>
    public static IReadOnlyList<LanguageFile> ParseTree(string output, CancellationToken ct = default)
    {
        var files = new List<LanguageFile>();
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
            if (language != null) files.Add(new LanguageFile(metadata[2], language, size));
        }
        return files;
    }

    public static IReadOnlyList<LanguageSummary> Summarize(IEnumerable<LanguageFile> files)
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var file in files) totals[file.Language] = totals.GetValueOrDefault(file.Language) + file.Bytes;
        var total = totals.Values.Sum();
        return totals.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new LanguageSummary(x.Key, x.Value, (double)x.Value / total)).ToList();
    }

    /// <summary>Reads <c>git cat-file --batch</c> output and returns the hashes of binary blobs.</summary>
    public static async Task<HashSet<string>> ReadBinaryBlobsAsync(Stream stream, CancellationToken ct = default)
    {
        var binary = new HashSet<string>(StringComparer.Ordinal);
        var buffer = new byte[64 * 1024];
        int start = 0, end = 0;

        async ValueTask<bool> FillAsync()
        {
            if (start < end) return true;
            start = 0;
            end = await stream.ReadAsync(buffer, ct);
            return end > 0;
        }

        async ValueTask<string?> ReadLineAsync()
        {
            var line = new StringBuilder();
            while (await FillAsync())
            {
                var newline = buffer.AsSpan(start, end - start).IndexOf((byte)'\n');
                var length = newline < 0 ? end - start : newline;
                line.Append(Encoding.ASCII.GetString(buffer, start, length));
                start += newline < 0 ? length : length + 1;
                if (newline >= 0) return line.ToString();
            }
            return line.Length == 0 ? null : throw new EndOfStreamException();
        }

        while (await ReadLineAsync() is { } header)
        {
            // "<hash> <type> <size>", followed by the content and a newline; "<hash> missing" has no content.
            var fields = header.Split(' ');
            if (fields.Length != 3) continue;
            var size = long.Parse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture);
            for (long read = 0; read <= size;)
            {
                if (!await FillAsync()) throw new EndOfStreamException();
                var n = (int)Math.Min(size + 1 - read, end - start);
                var sniff = (int)Math.Clamp(Math.Min(BinarySniffLength, size) - read, 0, n);
                if (buffer.AsSpan(start, sniff).Contains((byte)0)) binary.Add(fields[0]);
                start += n;
                read += n;
            }
        }
        return binary;
    }
}
