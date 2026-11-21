using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ClickToImages.Core.Rendering;
using Windows.Graphics.Imaging;

namespace ClickToImages.Core.Tests;

// Over RC4-128 PDFs that TestPdf encrypts itself; AES-256 has no fixture (see the last test).
public sealed class PdfPasswordTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("ClickToImagesPasswordTests").FullName;
    private readonly string lockedPath;

    public PdfPasswordTests()
    {
        lockedPath = Path.Combine(folder, "locked.pdf");
        TestPdf.WriteEncrypted(lockedPath, "secret", "owner", (300, 200, 0), (300, 200, 0));
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task OpenAsync_WithoutTheRightPassword_IsClassifiedAsPasswordRefused(string? password)
    {
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => PdfPageRenderer.OpenAsync(lockedPath, password));

        Assert.Equal(PdfOpenProblem.PasswordRefused, PdfOpenFailures.Classify(failure));
    }

    [Fact]
    public async Task OpenAsync_RightPassword_RendersTheDecryptedPage()
    {
        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(lockedPath, "secret");
        string output = Path.Combine(folder, "p1.png");

        await renderer.RenderToFileAsync(1, new PageRenderSettings { Dpi = 72 }, output);

        // The bottom-left square only draws if the content stream decrypted correctly.
        var decoder = await BitmapDecoder.CreateAsync(new MemoryStream(await File.ReadAllBytesAsync(output)).AsRandomAccessStream());
        PixelDataProvider pixels = await decoder.GetPixelDataAsync();
        byte[] data = pixels.DetachPixelData();
        int bottomLeft = ((190 * (int)decoder.PixelWidth) + 5) * 4;
        Assert.Equal(2, renderer.PageCount);
        Assert.Equal(0, data[bottomLeft + 2]);
    }

    [Fact]
    public async Task OpenAsync_EmptyUserPassword_OpensWithoutOne()
    {
        string permissionsOnly = Path.Combine(folder, "permissions-only.pdf");
        TestPdf.WriteEncrypted(permissionsOnly, "", "owner", (300, 200, 0));

        PdfPageRenderer renderer = await PdfPageRenderer.OpenAsync(permissionsOnly);

        Assert.Equal(1, renderer.PageCount);
    }

    [Fact]
    public async Task OpenAsync_WithPassword_LeavesSourcePdfUnchanged()
    {
        byte[] before = SHA256.HashData(File.ReadAllBytes(lockedPath));

        await PdfPageRenderer.OpenAsync(lockedPath, "secret");

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(lockedPath)));
    }

    [Fact]
    public async Task Classify_CorruptFile_IsNull()
    {
        string corrupt = Path.Combine(folder, "corrupt.pdf");
        File.WriteAllText(corrupt, "not a PDF");

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => PdfPageRenderer.OpenAsync(corrupt));

        Assert.Null(PdfOpenFailures.Classify(failure));
    }

    // 0x80048040 is what a real AES-256 PDF gave in M1 (spec §4.2); writing one here would need the
    // whole R6 key algorithm for one HRESULT, so the exception stands in for it.
    [Fact]
    public void Classify_Aes256Failure_IsUnsupportedEncryption()
    {
        var failure = new COMException("", unchecked((int)0x80048040));

        Assert.Equal(PdfOpenProblem.UnsupportedEncryption, PdfOpenFailures.Classify(failure));
    }
}
