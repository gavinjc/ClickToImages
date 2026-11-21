using System.Security.Cryptography;
using ClickToImages.Core.Rendering;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace ClickToImages.Core.Tests;

// Runs the real Windows.Data.Pdf over a generated PDF. Page sizes in points convert to pixels as
// points / 72 * dpi; each page has a distinct size so a wrong page index shows up as wrong pixels.
public sealed class PdfPageRendererTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("ClickToImagesTests").FullName;
    private readonly string pdfPath;

    public PdfPageRendererTests()
    {
        pdfPath = Path.Combine(folder, "fixture.pdf");
        TestPdf.Write(pdfPath, (612, 792, 0), (300, 200, 0), (300, 200, 90));
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    [Theory]
    [InlineData(816, 1056, 300, 2550u, 3300u)]
    [InlineData(816, 1056, 150, 1275u, 1650u)]
    [InlineData(400, 266.6667, 150, 625u, 417u)]
    public void PixelSize_ScalesNinetySixDpiViewUnitsByDpi(
        double width, double height, int dpi, uint expectedWidth, uint expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), PdfPageRenderer.PixelSize(new Size(width, height), dpi));
    }

    [Fact]
    public async Task RenderToFileAsync_SecondPage_HasThatPagesSizeAtRequestedDpi()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "p2.png");

        await renderer.RenderToFileAsync(2, new PageRenderSettings { Dpi = 150 }, output);

        BitmapDecoder decoder = await DecodeAsync(output);
        Assert.Equal(BitmapDecoder.PngDecoderId, decoder.DecoderInformation.CodecId);
        Assert.Equal((625u, 417u), (decoder.PixelWidth, decoder.PixelHeight));
    }

    [Theory]
    [InlineData(PageImageFormat.Png)]
    [InlineData(PageImageFormat.Jpeg)]
    [InlineData(PageImageFormat.Tiff)]
    public async Task RenderToFileAsync_WritesRequestedDpiIntoFile(PageImageFormat format)
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "p2" + format.FileExtension());

        await renderer.RenderToFileAsync(2, new PageRenderSettings { Dpi = 300, Format = format }, output);

        BitmapDecoder decoder = await DecodeAsync(output);
        Assert.Equal((300, 300), ((int)Math.Round(decoder.DpiX), (int)Math.Round(decoder.DpiY)));
    }

    [Fact]
    public async Task RenderToFileAsync_LowerJpegQuality_GivesSmallerFile()
    {
        TestPdf.WriteCheckerboard(pdfPath, 300, 200);
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string low = Path.Combine(folder, "low.jpg");
        string high = Path.Combine(folder, "high.jpg");

        await renderer.RenderToFileAsync(1, new PageRenderSettings { Format = PageImageFormat.Jpeg, JpegQuality = 20 }, low);
        await renderer.RenderToFileAsync(1, new PageRenderSettings { Format = PageImageFormat.Jpeg, JpegQuality = 95 }, high);

        Assert.True(new FileInfo(low).Length < new FileInfo(high).Length * 0.8,
            $"quality 20 gave {new FileInfo(low).Length} bytes, quality 95 gave {new FileInfo(high).Length}");
    }

    [Fact]
    public async Task RenderToFileAsync_RotatedPage_SwapsWidthAndHeight()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "p3.png");

        await renderer.RenderToFileAsync(3, new PageRenderSettings { Dpi = 150 }, output);

        BitmapDecoder decoder = await DecodeAsync(output);
        Assert.Equal((417u, 625u), (decoder.PixelWidth, decoder.PixelHeight));
    }

    // Page 2's black square sits at its bottom-left; turned clockwise that corner goes to the top
    // left, then the top right, then the bottom right.
    [Theory]
    [InlineData(90, 200u, 300u, 5, 5, 195, 295)]
    [InlineData(180, 300u, 200u, 295, 5, 5, 195)]
    [InlineData(270, 200u, 300u, 195, 295, 5, 5)]
    public async Task RenderToFileAsync_Rotation_TurnsTheImageClockwise(
        int degrees, uint expectedWidth, uint expectedHeight, int blackX, int blackY, int whiteX, int whiteY)
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "turned.png");

        await renderer.RenderToFileAsync(2, new PageRenderSettings { Dpi = 72, RotationDegrees = degrees }, output);

        BitmapDecoder decoder = await DecodeAsync(output);
        Assert.Equal((expectedWidth, expectedHeight), (decoder.PixelWidth, decoder.PixelHeight));
        Assert.Equal(0, (await PixelAsync(output, blackX, blackY))[2]);
        Assert.Equal(255, (await PixelAsync(output, whiteX, whiteY))[2]);
    }

    // A render 2 px wide and 3 px tall too many, as the probe's rounding can give. The overshoot is
    // on the unrotated right and bottom edges, so after a turn the crop must start past it.
    [Theory]
    [InlineData(0, 0u, 0u, 300u, 200u)]
    [InlineData(90, 3u, 0u, 200u, 300u)]
    [InlineData(180, 2u, 3u, 300u, 200u)]
    [InlineData(270, 0u, 2u, 200u, 300u)]
    public void CropBounds_CutsTheOvershootWhereverTheTurnCarriedIt(
        int degrees, uint expectedX, uint expectedY, uint expectedWidth, uint expectedHeight)
    {
        BitmapBounds bounds = PdfPageRenderer.CropBounds(302, 203, 300, 200, degrees);

        Assert.Equal((expectedX, expectedY, expectedWidth, expectedHeight), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    [Fact]
    public async Task RenderToStreamAsync_EncodesThePageIntoTheStream()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();

        await renderer.RenderToStreamAsync(2, new PageRenderSettings { Dpi = 150, RotationDegrees = 90 }, stream);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        Assert.Equal(BitmapDecoder.PngDecoderId, decoder.DecoderInformation.CodecId);
        Assert.Equal((417u, 625u), (decoder.PixelWidth, decoder.PixelHeight));
    }

    [Theory]
    [InlineData(PageImageFormat.Jpeg)]
    [InlineData(PageImageFormat.Tiff)]
    public async Task RenderToFileAsync_EncodesInRequestedFormat(PageImageFormat format)
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "p1" + format.FileExtension());

        await renderer.RenderToFileAsync(1, new PageRenderSettings { Dpi = 72, Format = format }, output);

        BitmapDecoder decoder = await DecodeAsync(output);
        Guid expectedDecoder = format == PageImageFormat.Jpeg ? BitmapDecoder.JpegDecoderId : BitmapDecoder.TiffDecoderId;
        Assert.Equal(expectedDecoder, decoder.DecoderInformation.CodecId);
        Assert.Equal((612u, 792u), (decoder.PixelWidth, decoder.PixelHeight));
    }

    [Theory]
    [InlineData(false, 255, 255)]
    [InlineData(true, 0, 0)]
    public async Task RenderToFileAsync_Background_IsOpaqueWhiteUnlessTransparent(
        bool transparent, byte expectedAlpha, byte expectedRed)
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "bg.png");

        await renderer.RenderToFileAsync(2, new PageRenderSettings { Dpi = 72, TransparentBackground = transparent }, output);

        // Top-right is background; the bottom-left black square proves the page drew at all.
        byte[] topRight = await PixelAsync(output, x: 290, y: 5);
        byte[] bottomLeft = await PixelAsync(output, x: 5, y: 190);
        Assert.Equal((expectedAlpha, expectedRed), (topRight[3], PremultipliedRed(topRight)));
        Assert.Equal((255, 0), (bottomLeft[3], bottomLeft[2]));
    }

    [Fact]
    public async Task RenderToFileAsync_TransparentJpeg_ThrowsWithoutWritingFile()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "p1.jpg");
        var settings = new PageRenderSettings { Format = PageImageFormat.Jpeg, TransparentBackground = true };

        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderToFileAsync(1, settings, output));

        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task RenderToFileAsync_PageOutsideDocument_ThrowsWithoutWritingFile(int pageNumber)
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);
        string output = Path.Combine(folder, "out.png");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => renderer.RenderToFileAsync(pageNumber, new PageRenderSettings(), output));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task WriteEncodedAsync_CancelledMidWrite_DeletesPartialFile()
    {
        string output = Path.Combine(folder, "partial.png");
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 10, 10, BitmapAlphaMode.Ignore);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PdfPageRenderer.WriteEncodedAsync(bitmap, new PageRenderSettings(), output, new CancellationToken(true)));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task RenderToFileAsync_LeavesSourcePdfUnchanged()
    {
        byte[] before = SHA256.HashData(File.ReadAllBytes(pdfPath));
        DateTime writtenBefore = File.GetLastWriteTimeUtc(pdfPath);
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);

        await renderer.RenderToFileAsync(1, new PageRenderSettings(), Path.Combine(folder, "p1.png"));

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(pdfPath)));
        Assert.Equal(writtenBefore, File.GetLastWriteTimeUtc(pdfPath));
    }

    // Page 1 is portrait, page 2 landscape, page 3 landscape turned portrait by /Rotate. Off by
    // more than a pixel means the renderer's own display scale went uncorrected or the long edge
    // was taken from the wrong side.
    [Theory]
    [InlineData(1, 232, 300)]
    [InlineData(2, 300, 200)]
    [InlineData(3, 200, 300)]
    public async Task RenderScaledPngAsync_FitsTheLongerEdgeToTheRequestedPixels(
        int pageNumber, int expectedWidth, int expectedHeight)
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);

        byte[] bitmap = await renderer.RenderScaledPngAsync(pageNumber, 300);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(new MemoryStream(bitmap).AsRandomAccessStream());
        Assert.InRange((int)decoder.PixelWidth, expectedWidth - 1, expectedWidth + 1);
        Assert.InRange((int)decoder.PixelHeight, expectedHeight - 1, expectedHeight + 1);
    }

    [Fact]
    public async Task PageCount_IsNumberOfPagesInDocument()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(pdfPath);

        Assert.Equal(3, renderer.PageCount);
    }

    private static async Task<BitmapDecoder> DecodeAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        return await BitmapDecoder.CreateAsync(new MemoryStream(bytes).AsRandomAccessStream());
    }

    // BGRA, straight alpha.
    private static async Task<byte[]> PixelAsync(string path, int x, int y)
    {
        BitmapDecoder decoder = await DecodeAsync(path);
        PixelDataProvider pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        int offset = ((y * (int)decoder.PixelWidth) + x) * 4;
        return pixels.DetachPixelData()[offset..(offset + 4)];
    }

    // A fully transparent pixel's colour channels are meaningless, so compare red only once it is
    // weighted by alpha: 0 for transparent, 255 for opaque white.
    private static byte PremultipliedRed(byte[] bgra) => (byte)(bgra[2] * bgra[3] / 255);
}
