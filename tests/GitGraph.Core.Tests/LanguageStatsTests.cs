using GitGraph.Core.Stats;

namespace GitGraph.Core.Tests;

public sealed class LanguageStatsTests
{
    [Fact]
    public void Counts_blob_bytes_and_ignores_dependencies_generated_files_and_links()
    {
        var tree = string.Join('\0',
            "100644 blob abc 80\tsrc/hello 世界.cs",
            "100644 blob abc 20\tscripts/a\tb.py",
            "100644 blob abc 999\tvendor/lib.js",
            "100644 blob abc 999\tsrc/File.g.cs",
            "100644 blob abc 999\tREADME.md",
            "120000 blob abc 999\tlink.cs",
            "160000 commit abc -\tsubmodule",
            "100644 blob abc 0\tempty.rs", "");

        var result = LanguageStats.ParseTree(tree);

        Assert.Equal(2, result.Count);
        Assert.Equal("C#", result[0].Name);
        Assert.Equal(80, result[0].Bytes);
        Assert.Equal(0.8, result[0].Share);
        Assert.Equal("Python", result[1].Name);
        Assert.Equal(0.2, result[1].Share);
    }

    [Fact]
    public void Recognizes_build_filenames_and_orders_ties_by_name()
    {
        var result = LanguageStats.ParseTree("100644 blob abc 10\tMakefile\0" +
            "100644 blob abc 10\tDockerfile\0" + "100644 blob abc 10\tsrc/a.C\0");
        Assert.Equal(new[] { "C++", "Dockerfile", "Makefile" }, result.Select(x => x.Name));
        Assert.Empty(LanguageStats.ParseTree(""));
    }
}
