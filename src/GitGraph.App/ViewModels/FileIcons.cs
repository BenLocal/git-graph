using Lucide.Avalonia;

namespace GitGraph.App.ViewModels;

/// <summary>The icon a file's row shows in a folder tab's tree, picked from its name as file explorers do.</summary>
public static class FileIcons
{
    private static readonly Dictionary<string, LucideIconKind> ByExtension = Build(
        (LucideIconKind.FileCode,
        [
            "cs", "csx", "fs", "fsx", "vb", "go", "rs", "java", "kt", "kts", "scala", "groovy", "c", "h", "cc", "cpp", "cxx",
            "hpp", "hh", "m", "mm", "swift", "py", "pyi", "rb", "php", "pl", "lua", "dart", "zig", "nim", "ex", "exs",
            "erl", "hs", "clj", "r", "jl", "js", "mjs", "cjs", "jsx", "ts", "mts", "cts", "tsx", "vue", "svelte", "html",
            "htm", "css", "scss", "sass", "less", "xaml", "axaml", "razor", "cshtml", "xml", "sql", "proto", "graphql",
        ]),
        (LucideIconKind.FileBraces, ["json", "jsonc", "json5"]),
        (LucideIconKind.FileCog,
        [
            "yml", "yaml", "toml", "ini", "cfg", "conf", "config", "properties", "env", "editorconfig", "props", "targets",
            "csproj", "fsproj", "vbproj", "vcxproj", "sln", "slnx", "plist", "manifest", "gradle", "cmake", "mk",
        ]),
        (LucideIconKind.FileTerminal, ["sh", "bash", "zsh", "fish", "ps1", "psm1", "bat", "cmd"]),
        (LucideIconKind.FileText, ["md", "markdown", "mdx", "txt", "rst", "adoc", "org", "pdf", "rtf", "doc", "docx"]),
        (LucideIconKind.FileImage, ["png", "jpg", "jpeg", "gif", "bmp", "ico", "icns", "svg", "webp", "tif", "tiff", "avif"]),
        (LucideIconKind.FileArchive, ["zip", "tar", "gz", "tgz", "bz2", "xz", "zst", "7z", "rar", "jar", "war", "nupkg"]),
        (LucideIconKind.FileLock, ["lock"]),
        (LucideIconKind.FileSpreadsheet, ["csv", "tsv", "xls", "xlsx", "ods"]),
        (LucideIconKind.FileType, ["ttf", "otf", "woff", "woff2"]),
        (LucideIconKind.FileVideoCamera, ["mp4", "mov", "avi", "mkv", "webm"]),
        (LucideIconKind.FileMusic, ["mp3", "wav", "flac", "ogg", "m4a"]),
        (LucideIconKind.FileKey, ["pem", "key", "crt", "cer", "p12", "pfx", "pub"]),
        (LucideIconKind.FileDiff, ["diff", "patch"]));

    /// <summary>Names that say more than their extension, or have none.</summary>
    private static readonly Dictionary<string, LucideIconKind> ByName = Build(
        (LucideIconKind.FileCog,
        [
            "Makefile", "GNUmakefile", "Dockerfile", "Containerfile", "Justfile", "Rakefile", "Gemfile", "Procfile",
            "CMakeLists.txt", ".gitignore", ".gitattributes", ".gitmodules", ".dockerignore", ".npmrc", ".nvmrc",
        ]),
        (LucideIconKind.FileLock, ["package-lock.json", "go.sum"]),
        (LucideIconKind.FileText, ["README", "LICENSE", "LICENCE", "COPYING", "CHANGELOG", "AUTHORS", "NOTICE"]));

    public static LucideIconKind For(string name)
    {
        if (ByName.TryGetValue(name, out var kind)) return kind;
        var dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 && ByExtension.TryGetValue(name[(dot + 1)..], out kind)
            ? kind
            : LucideIconKind.File;
    }

    private static Dictionary<string, LucideIconKind> Build(params (LucideIconKind Kind, string[] Keys)[] groups)
    {
        var map = new Dictionary<string, LucideIconKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, keys) in groups)
            foreach (var key in keys) map[key] = kind;
        return map;
    }
}
