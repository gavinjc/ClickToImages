using ClickToImages.Core.Export;
using ClickToImages.Core.Rendering;

namespace ClickToImages.Core.Tests;

public class DocumentNamesTests
{
    private const string Out = @"C:\Temp\Test";

    [Fact]
    public void Distinguish_TwoSameNamedPdfsIntoOneFolder_SuffixesTheSecond()
    {
        IReadOnlyList<string> names = DocumentNames.Distinguish(
            [(@"C:\X\report.pdf", Out), (@"C:\Y\report.pdf", Out)]);

        Assert.Equal(["report", "report (2)"], names);
    }

    [Fact]
    public void Distinguish_ThreeSameNamedPdfs_NumbersThemInTheOrderGiven_IgnoringCase()
    {
        IReadOnlyList<string> names = DocumentNames.Distinguish(
            [(@"C:\Y\report.pdf", Out), (@"C:\X\REPORT.pdf", Out), (@"C:\Z\Report.PDF", Out)]);

        Assert.Equal(["report", "REPORT (2)", "Report (3)"], names);
    }

    [Fact]
    public void Distinguish_SkipsANumberAnotherPdfAlreadyHasAsItsOwnName()
    {
        IReadOnlyList<string> names = DocumentNames.Distinguish(
            [(@"C:\X\report.pdf", Out), (@"C:\Y\report.pdf", Out), (@"C:\Z\report (2).pdf", Out)]);

        Assert.Equal(["report", "report (3)", "report (2)"], names);
    }

    // Subfolder or beside-the-PDF output: the images can't clash, so they keep their plain names.
    [Fact]
    public void Distinguish_SameNamedPdfsIntoDifferentFolders_KeepTheirNames()
    {
        IReadOnlyList<string> names = DocumentNames.Distinguish(
            [(@"C:\X\report.pdf", @"C:\X\report"), (@"C:\Y\report.pdf", @"C:\Y\report")]);

        Assert.Equal(["report", "report"], names);
    }

    [Fact]
    public void Distinguish_ThenTheDefaultPattern_GivesEveryPageItsOwnPath()
    {
        var pattern = new FileNamePattern(FileNamePattern.Default);
        IReadOnlyList<string> names = DocumentNames.Distinguish(
            [(@"C:\X\report.pdf", Out), (@"C:\Y\report.pdf", Out)]);

        List<string> paths = names.SelectMany(name => Enumerable.Range(1, 3)
            .Select(page => Path.Combine(Out, pattern.FileName(name, page, 3, 150, PageImageFormat.Png)))).ToList();

        Assert.Equal(6, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
