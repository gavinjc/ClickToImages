using ClickToImages.Core.Export;
using ClickToImages.Core.Rendering;

namespace ClickToImages.Core.Tests;

// Runs real renders into a temp folder. The fixture has 3 pages, so page 99 is a render that fails
// partway through an export.
public sealed class PageExporterTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("ClickToImagesExportTests").FullName;
    private readonly string pdfPath;

    public PageExporterTests()
    {
        pdfPath = Path.Combine(folder, "fixture.pdf");
        TestPdf.Write(pdfPath, (300, 200, 0), (300, 200, 0), (300, 200, 0));
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task ExportAsync_WritesEveryPageToItsPath_AndNoTempFiles()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "out");
        var reported = new List<int>();

        await PageExporter.ExportAsync(
            [Page(renderer, 1, output, "a.png"), Page(renderer, 3, output, "b.png")],
            new PageRenderSettings { Dpi = 72 }, new SynchronousProgress(reported.Add), CancellationToken.None);

        Assert.Equal(["a.png", "b.png"], Directory.GetFiles(output).Select(Path.GetFileName).Order());
        Assert.Equal([1, 2], reported);
    }

    [Fact]
    public async Task ExportAsync_AppliesEachPagesOwnRotation()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);

        await PageExporter.ExportAsync(
            [new PageExport(renderer, 1, 90, Path.Combine(folder, "turned.png"))],
            new PageRenderSettings { Dpi = 72 }, null, CancellationToken.None);

        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(
            new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(folder, "turned.png"))).AsRandomAccessStream());
        Assert.Equal((200u, 300u), (decoder.PixelWidth, decoder.PixelHeight));
    }

    [Fact]
    public async Task ExportAsync_CancelledAfterOnePage_LeavesNoFilesAndRemovesTheFolderItMade()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "out");
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PageExporter.ExportAsync(
            [Page(renderer, 1, output, "a.png"), Page(renderer, 2, output, "b.png"), Page(renderer, 3, output, "c.png")],
            new PageRenderSettings { Dpi = 72 }, new SynchronousProgress(_ => cancellation.Cancel()), cancellation.Token));

        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task ExportAsync_FailingPartway_KeepsTheFileItWouldHaveReplaced()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string existing = Path.Combine(folder, "a.png");
        File.WriteAllText(existing, "the user's own file");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PageExporter.ExportAsync(
            [Page(renderer, 1, folder, "a.png"), Page(renderer, 99, folder, "b.png")],
            new PageRenderSettings { Dpi = 72 }, null, CancellationToken.None));

        Assert.Equal("the user's own file", File.ReadAllText(existing));
        Assert.Equal(["a.png", "fixture.pdf"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
    }

    // Every page renders, then b.png can't be replaced because something holds it open. a.png has
    // already been replaced by then, and must get its old contents back.
    [Fact]
    public async Task ExportAsync_FailingWhileMovingIntoPlace_PutsBackEveryFileItReplaced()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        File.WriteAllText(Path.Combine(folder, "a.png"), "old a");
        File.WriteAllText(Path.Combine(folder, "b.png"), "old b");

        using (new FileStream(Path.Combine(folder, "b.png"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => PageExporter.ExportAsync(
                [Page(renderer, 1, folder, "a.png"), Page(renderer, 2, folder, "b.png")],
                new PageRenderSettings { Dpi = 72 }, null, CancellationToken.None));
        }

        Assert.Equal(("old a", "old b"),
            (File.ReadAllText(Path.Combine(folder, "a.png")), File.ReadAllText(Path.Combine(folder, "b.png"))));
        Assert.Equal(["a.png", "b.png", "fixture.pdf"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task ExportAsync_Completed_ReplacesAnExistingFile()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string existing = Path.Combine(folder, "a.png");
        File.WriteAllText(existing, "old");

        await PageExporter.ExportAsync(
            [Page(renderer, 1, folder, "a.png")], new PageRenderSettings { Dpi = 72 }, null, CancellationToken.None);

        Assert.NotEqual("old", File.ReadAllText(existing));
    }

    [Fact]
    public async Task ExportAsync_TwoPagesToOnePath_ThrowsBeforeWritingAnything()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "out");

        await Assert.ThrowsAsync<ArgumentException>(() => PageExporter.ExportAsync(
            [Page(renderer, 1, output, "same.png"), Page(renderer, 2, output, "SAME.png")],
            new PageRenderSettings(), null, CancellationToken.None));

        Assert.False(Directory.Exists(output));
    }

    // The guard ExportAsync keeps for a caller that skipped FindNameClash.
    [Fact]
    public async Task ExportAsync_RefusesTwoPagesWithOnePath_BeforeWritingAnything()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "out");

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => PageExporter.ExportAsync(
            [Page(renderer, 1, output, "scan.png"), Page(renderer, 2, output, "scan.png")],
            new PageRenderSettings { Dpi = 72 }, null, CancellationToken.None));

        Assert.StartsWith($"Two pages would both be saved as {Path.Combine(output, "scan.png")}.", refused.Message);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task FindNameClash_PatternWithoutPage_AdvisesAddingPage()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);

        NameClash? clash = PageExporter.FindNameClash(
            [Page(renderer, 1, folder, "fixture.png"), Page(renderer, 2, folder, "fixture.png")],
            new FileNamePattern("{name}"));

        Assert.Equal(new NameClash(Path.Combine(folder, "fixture.png"), NameClashFix.AddPage), clash);
    }

    [Fact]
    public async Task FindNameClash_TwoPdfsWithoutName_AdvisesAddingName()
    {
        PdfPageRenderer first = await PdfPageRenderer.OpenAsync(pdfPath);
        PdfPageRenderer second = await PdfPageRenderer.OpenAsync(pdfPath);

        NameClash? clash = PageExporter.FindNameClash(
            [Page(first, 1, folder, "p1.png"), Page(second, 1, folder, "p1.png")],
            new FileNamePattern("p{page}"));

        Assert.Equal(NameClashFix.AddName, clash?.Fix);
    }

    // report1's page 2 and report's page 12 under {name}{page}: both tokens are there, so adding
    // either would not help.
    [Fact]
    public async Task FindNameClash_WithBothTokens_AdvisesRewording()
    {
        PdfPageRenderer first = await PdfPageRenderer.OpenAsync(pdfPath);
        PdfPageRenderer second = await PdfPageRenderer.OpenAsync(pdfPath);

        NameClash? clash = PageExporter.FindNameClash(
            [Page(first, 2, folder, "report12.png"), Page(second, 12, folder, "REPORT12.png")],
            new FileNamePattern("{name}{page}"));

        Assert.Equal(NameClashFix.Reword, clash?.Fix);
    }

    [Fact]
    public async Task FindNameClash_NullWhenEveryPathDiffers()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);

        Assert.Null(PageExporter.FindNameClash(
            [Page(renderer, 1, folder, "a.png"), Page(renderer, 2, folder, "b.png")],
            new FileNamePattern("{page}")));
    }

    private static PageExport Page(PdfPageRenderer renderer, int pageNumber, string folder, string name) =>
        new(renderer, pageNumber, 0, Path.Combine(folder, name));

    // Progress<T> posts to the thread pool, so a cancel from it would land at no particular page.
    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
