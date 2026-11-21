using ClickToImages.Core.Rendering;

namespace ClickToImages.Core.Tests;

// The messages asserted are Windows' own, in English.
public sealed class PdfOpenFailuresTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("ClickToImagesOpenFailureTests").FullName;

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    // A zero-length file's message is "The text associated with this error code could not be
    // found.\r\n\r\nInvalid file, zero length. 0x8004808e"; a junk one's is empty.
    [Theory]
    [InlineData("")]
    [InlineData("not a PDF")]
    public async Task ReadableReason_BadPdf_IsNull(string content)
    {
        string path = Path.Combine(folder, "bad.pdf");
        File.WriteAllText(path, content);

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => PdfPageRenderer.OpenAsync(path));

        Assert.Null(PdfOpenFailures.ReadableReason(failure));
    }

    // Windows gives "Access is denied.\r\n\r\nFailed opening file 0x80070005"; only the sentence is shown.
    [Fact]
    public async Task ReadableReason_FileHeldOpenElsewhere_IsTheFirstSentenceOnly()
    {
        string path = Path.Combine(folder, "held.pdf");
        File.WriteAllText(path, "%PDF-1.4");
        using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => PdfPageRenderer.OpenAsync(path));

        Assert.Equal("Access is denied.", PdfOpenFailures.ReadableReason(failure));
    }
}
