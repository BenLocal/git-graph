using System.Text;
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

        var result = LanguageStats.Summarize(LanguageStats.ParseTree(tree));

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
        var result = LanguageStats.Summarize(LanguageStats.ParseTree("100644 blob abc 10\tMakefile\0" +
            "100644 blob abc 10\tDockerfile\0" + "100644 blob abc 10\tsrc/a.C\0"));
        Assert.Equal(new[] { "C++", "Dockerfile", "Makefile" }, result.Select(x => x.Name));
        Assert.Empty(LanguageStats.ParseTree(""));
    }

    [Fact]
    public async Task Binary_blobs_have_a_nul_byte_in_their_first_8000_bytes()
    {
        static byte[] Blob(string hash, byte[] content) =>
            [.. Encoding.ASCII.GetBytes($"{hash} blob {content.Length}\n"), .. content, (byte)'\n'];
        var lateNul = new byte[8001];
        lateNul.AsSpan().Fill((byte)'a');
        lateNul[8000] = 0;
        var largeVideo = new byte[200_000];
        largeVideo[7999] = 0x47;
        largeVideo[0] = 0x47;
        using var stream = new MemoryStream([
            .. Blob("text", "const a = 1;\n"u8.ToArray()),
            .. Blob("video", largeVideo),
            .. "gone missing\n"u8.ToArray(),
            .. Blob("late", lateNul),
            .. Blob("nul", [0x47, 0, 0x10]),
        ]);

        var binary = await LanguageStats.ReadBinaryBlobsAsync(stream);

        Assert.Equal(new[] { "nul", "video" }, binary.Order());
    }
}
