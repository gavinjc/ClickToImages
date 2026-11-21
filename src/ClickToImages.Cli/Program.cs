using System.Diagnostics;
using System.Runtime.InteropServices;
using ClickToImages.Core;
using ClickToImages.Core.Export;
using ClickToImages.Core.Rendering;
using Windows.Foundation;

// The M1 proving ground (spec §6): render pages of one PDF to disk with no UI, and print enough
// timing and size detail to check the output against spec §5's targets.
string? pdfPath = null;
string? pagesText = null;
string? outputFolder = null;
var settings = new PageRenderSettings();

try
{
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--pages" when i + 1 < args.Length:
                pagesText = args[++i];
                break;
            case "--dpi" when i + 1 < args.Length:
                settings = settings with { Dpi = int.Parse(args[++i]) };
                break;
            case "--format" when i + 1 < args.Length:
                settings = settings with { Format = ParseFormat(args[++i]) };
                break;
            case "--quality" when i + 1 < args.Length:
                settings = settings with { JpegQuality = int.Parse(args[++i]) };
                break;
            case "--transparent":
                settings = settings with { TransparentBackground = true };
                break;
            case "--output" when i + 1 < args.Length:
                outputFolder = args[++i];
                break;
            case var arg when !arg.StartsWith("--", StringComparison.Ordinal) && pdfPath is null:
                pdfPath = arg;
                break;
            default:
                throw new ArgumentException($"Unexpected argument '{args[i]}'.");
        }
    }
}
catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
{
    Console.Error.WriteLine($"Bad argument: {ex.Message}");
    return 1;
}

if (pdfPath is null)
{
    Console.Error.WriteLine("Usage: ClickToImages.Cli <file.pdf> [--pages 1-4,7,12-] [--dpi 150]");
    Console.Error.WriteLine("                         [--format png|jpeg|tiff] [--quality 1-100] [--transparent]");
    Console.Error.WriteLine("                         [--output <folder>]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Defaults: every page, 150 DPI, PNG (JPEG quality 90), into a folder named after the PDF beside it.");
    return 1;
}

if (settings.TransparentBackground && !settings.Format.SupportsTransparency())
{
    Console.Error.WriteLine($"Bad argument: --transparent needs PNG; {settings.Format} has no transparency.");
    return 1;
}

if (!File.Exists(pdfPath) || !SupportedExtensions.Includes(pdfPath))
{
    Console.Error.WriteLine($"Not a PDF file: {pdfPath}");
    return 1;
}

PdfPageRenderer renderer;
try
{
    renderer = await PdfPageRenderer.OpenAsync(pdfPath);
}
catch (Exception ex) when (IsFileOrRenderFailure(ex))
{
    Console.Error.WriteLine($"Could not open {pdfPath}: {Describe(ex)}");
    return 1;
}

IReadOnlyList<int> pageNumbers;
try
{
    pageNumbers = pagesText is null
        ? Enumerable.Range(1, renderer.PageCount).ToArray()
        : PageRange.Parse(pagesText, renderer.PageCount);
}
catch (FormatException ex)
{
    Console.Error.WriteLine($"Bad --pages: {ex.Message}");
    return 1;
}

outputFolder ??= OutputLocation.SubfolderNamedAfterPdf.FolderFor(pdfPath, chosenFolder: null);
try
{
    Directory.CreateDirectory(outputFolder);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Could not create {outputFolder}: {ex.Message}");
    return 1;
}

Console.WriteLine($"{pdfPath}: {renderer.PageCount} page(s); rendering {pageNumbers.Count} at {settings.Dpi} DPI " +
    $"{settings.Format}{(settings.TransparentBackground ? " (transparent)" : "")} -> {outputFolder}");

var naming = new FileNamePattern(FileNamePattern.Default);
Stopwatch total = Stopwatch.StartNew();
long totalBytes = 0;
foreach (int pageNumber in pageNumbers)
{
    string outputPath = Path.Combine(
        outputFolder, naming.FileName(Path.GetFileNameWithoutExtension(pdfPath), pageNumber, renderer.PageCount, settings.Dpi, settings.Format));
    Size pageSize = await renderer.GetPageSizeAsync(pageNumber);
    (uint width, uint height) = PdfPageRenderer.PixelSize(pageSize, settings.Dpi);

    Stopwatch pageTimer = Stopwatch.StartNew();
    try
    {
        await renderer.RenderToFileAsync(pageNumber, settings, outputPath);
    }
    catch (Exception ex) when (IsFileOrRenderFailure(ex))
    {
        Console.Error.WriteLine($"Page {pageNumber} failed: {Describe(ex)}");
        return 1;
    }

    long bytes = new FileInfo(outputPath).Length;
    totalBytes += bytes;
    Console.WriteLine($"  p{pageNumber,-4} {width,5}x{height,-5} {pageTimer.ElapsedMilliseconds,6} ms {bytes,13:N0} bytes");
}

Console.WriteLine($"Done: {pageNumbers.Count} page(s) in {total.Elapsed:mm\\:ss\\.fff}, {totalBytes:N0} bytes, " +
    $"peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MB");
return 0;

static PageImageFormat ParseFormat(string value) => value switch
{
    "png" => PageImageFormat.Png,
    "jpeg" or "jpg" => PageImageFormat.Jpeg,
    "tiff" or "tif" => PageImageFormat.Tiff,
    _ => throw new ArgumentException($"Unknown --format value '{value}'."),
};

// InvalidOperationException is PdfPageRenderer's short-render check: the render-scale workaround
// (spec §4.2) not holding on this machine, which is exactly what a CLI run should report cleanly.
static bool IsFileOrRenderFailure(Exception ex) =>
    ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException;

// Windows.Data.Pdf's COM errors arrive with an empty message, so the HRESULT is all there is to go
// on. 0x80048040 is what an AES-256 (R6) encrypted PDF gives, with or without a password.
static string Describe(Exception ex) => ex is COMException
    ? $"HRESULT 0x{ex.HResult:X8} {ex.Message}".TrimEnd()
    : ex.Message;
