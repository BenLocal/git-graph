using GitGraph.App.ViewModels;
using Lucide.Avalonia;

namespace GitGraph.App.Tests;

public sealed class FileIconsTests
{
    [Theory]
    [InlineData("App.cs", LucideIconKind.FileCode)]
    [InlineData("FolderTabView.axaml", LucideIconKind.FileCode)]
    [InlineData("package.json", LucideIconKind.FileBraces)]
    [InlineData("package-lock.json", LucideIconKind.FileLock)]
    [InlineData("Cargo.lock", LucideIconKind.FileLock)]
    [InlineData("GitGraph.App.csproj", LucideIconKind.FileCog)]
    [InlineData("Makefile", LucideIconKind.FileCog)]
    [InlineData(".gitignore", LucideIconKind.FileCog)]
    [InlineData(".env", LucideIconKind.FileCog)]
    [InlineData("build.sh", LucideIconKind.FileTerminal)]
    [InlineData("README.md", LucideIconKind.FileText)]
    [InlineData("LICENSE", LucideIconKind.FileText)]
    [InlineData("Screenshot.PNG", LucideIconKind.FileImage)]
    [InlineData("release.tar.gz", LucideIconKind.FileArchive)]
    [InlineData("fix.patch", LucideIconKind.FileDiff)]
    // Unknown kinds, hidden files without a known name and names ending in a dot keep the plain file icon.
    [InlineData("data.bin", LucideIconKind.File)]
    [InlineData(".bashrc", LucideIconKind.File)]
    [InlineData("notes.", LucideIconKind.File)]
    public void A_file_gets_the_icon_of_its_kind(string name, LucideIconKind expected) => Assert.Equal(expected, FileIcons.For(name));
}
