using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;

namespace ClickToImages.Core.Rendering;

/// <summary>
/// One open PDF and the only way to render its pages. <see cref="PdfDocument"/> is not safe to
/// render from several threads at once (spec §5): it corrupts output intermittently rather than
/// failing. Every page access therefore goes through this instance's gate, so callers can fan out
/// across documents freely but never within one.
/// </summary>
public sealed class PdfPageRenderer
{
    // Wide enough that the measured scale is accurate to 1/10000, so a 600-DPI A3 page still
    // lands within a pixel. One pixel tall, but not free: on a scanned page Windows.Data.Pdf keeps
    // about 90 MB for the process's life after a probe this wide (measured on an A4 300-DPI scan).
    private const uint ExactScaleProbeWidth = 10_000;

    // Accurate to 1/1000: within a pixel or so for the grid's thumbnails and a full-screen
    // preview, which is all RenderScaledPngAsync promises. On the same scan it keeps about 15 MB,
    // which took a 200-page grid's peak working set down by about 70 MB.
    private const uint ScaledScaleProbeWidth = 1_000;

    private readonly PdfDocument document;
    private readonly SemaphoreSlim pageGate = new(1, 1);

    // Measured on the first RenderScaledPngAsync and reused: a probe per thumbnail would double the
    // renders a 200-page grid costs. Only read and written under pageGate.
    private double? scaledRenderScale;

    private PdfPageRenderer(PdfDocument document)
    {
        this.document = document;
    }

    public int PageCount => (int)document.PageCount;

    // GetFileFromPathAsync + LoadFromFileAsync open the file for reading only; the source PDF is
    // never written (spec §10). The password goes to Windows.Data.Pdf and nowhere else: this class
    // doesn't keep it (spec §10). PdfOpenFailures says what a failure means.
    public static async Task<PdfPageRenderer> OpenAsync(string pdfPath, string? password = null)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
        PdfDocument document = password is null
            ? await PdfDocument.LoadFromFileAsync(file)
            : await PdfDocument.LoadFromFileAsync(file, password);
        return new PdfPageRenderer(document);
    }

    /// <summary>
    /// <see cref="PdfPage.Size"/> is in 96-DPI view units and already reflects the page's crop box
    /// and /Rotate, so the pixel size wanted at a DPI is a plain multiplication.
    /// </summary>
    public static (uint Width, uint Height) PixelSize(Size pageSize, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        return (ScaleToPixels(pageSize.Width, dpi), ScaleToPixels(pageSize.Height, dpi));
    }

    public async Task<Size> GetPageSizeAsync(int pageNumber)
    {
        uint pageIndex = PageIndex(pageNumber);
        await pageGate.WaitAsync();
        try
        {
            using PdfPage page = document.GetPage(pageIndex);
            return page.Size;
        }
        finally
        {
            pageGate.Release();
        }
    }

    /// <summary>
    /// Renders one page (1-based) to <paramref name="outputPath"/> at exactly
    /// <see cref="PixelSize"/> for the requested DPI, overwriting the file. One page is held in
    /// memory at a time. On any failure, cancellation included, the partial file is deleted.
    /// </summary>
    public Task RenderToFileAsync(
        int pageNumber, PageRenderSettings settings, string outputPath, CancellationToken cancellationToken = default) =>
        RenderExactThenAsync(
            pageNumber, settings, bitmap => WriteEncodedAsync(bitmap, settings, outputPath, cancellationToken),
            cancellationToken);

    /// <summary>
    /// As <see cref="RenderToFileAsync"/>, but encoded into <paramref name="output"/>: the
    /// clipboard's copy of a page, which never touches the disk.
    /// </summary>
    public Task RenderToStreamAsync(
        int pageNumber, PageRenderSettings settings, IRandomAccessStream output,
        CancellationToken cancellationToken = default) =>
        RenderExactThenAsync(
            pageNumber, settings, bitmap => EncodeAsync(bitmap, settings, output, cancellationToken), cancellationToken);

    private async Task RenderExactThenAsync(
        int pageNumber, PageRenderSettings settings, Func<SoftwareBitmap, Task> encode,
        CancellationToken cancellationToken)
    {
        if (settings.TransparentBackground && !settings.Format.SupportsTransparency())
        {
            throw new ArgumentException($"{settings.Format} has no transparency; only PNG does.", nameof(settings));
        }

        uint pageIndex = PageIndex(pageNumber);
        await pageGate.WaitAsync(cancellationToken);
        try
        {
            using PdfPage page = document.GetPage(pageIndex);
            using SoftwareBitmap bitmap = await RenderExactAsync(page, settings, cancellationToken);
            await encode(bitmap);
        }
        finally
        {
            // The BitmapDecoder has no Dispose and keeps the page-sized render stream alive until
            // its finalizer runs, and native memory puts no pressure on the GC. Without this, 600-DPI
            // A4 pages piled up at ~150 MB each to over 1 GB; spec §5 allows 600 MB and one page.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            pageGate.Release();
        }
    }

    /// <summary>
    /// Renders one page (1-based) small, for the page grid and the preview overlay, and returns it
    /// as encoded PNG bytes for the UI to decode. PNG, not the cheaper-to-encode BMP: WinUI's
    /// BitmapImage keeps its encoded source alive beside the pixels, and with BMP that doubled the
    /// grid's memory (a 200-page scan peaked at 663 MB against spec §5's 600; 524 MB as PNG, no
    /// slower). Its longer edge is
    /// <paramref name="longEdgePixels"/>, give or take a pixel; no exact DPI is promised, so unlike
    /// <see cref="RenderToFileAsync"/> it has no crop and no forced GC, which a 200-page grid
    /// cannot afford per page. Still serialised through the page gate.
    /// </summary>
    public async Task<byte[]> RenderScaledPngAsync(
        int pageNumber, uint longEdgePixels, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(longEdgePixels);
        uint pageIndex = PageIndex(pageNumber);
        await pageGate.WaitAsync(cancellationToken);
        try
        {
            using PdfPage page = document.GetPage(pageIndex);
            scaledRenderScale ??= await MeasureRenderScaleAsync(page, ScaledScaleProbeWidth, cancellationToken);
            double longEdgeViewUnits = longEdgePixels / scaledRenderScale.Value;

            // Setting only one destination dimension keeps the page's aspect ratio.
            var options = new PdfPageRenderOptions { BitmapEncoderId = BitmapEncoder.PngEncoderId };
            if (page.Size.Width >= page.Size.Height)
            {
                options.DestinationWidth = (uint)Math.Max(1, Math.Round(longEdgeViewUnits));
            }
            else
            {
                options.DestinationHeight = (uint)Math.Max(1, Math.Round(longEdgeViewUnits));
            }

            using var rendered = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered, options).AsTask(cancellationToken);
            var bytes = new byte[rendered.Size];
            using DataReader reader = new(rendered.GetInputStreamAt(0));
            await reader.LoadAsync((uint)rendered.Size).AsTask(cancellationToken);
            reader.ReadBytes(bytes);
            return bytes;
        }
        finally
        {
            pageGate.Release();
        }
    }

    /// <summary>
    /// RenderToStreamAsync treats DestinationWidth/Height as view units and multiplies them by a
    /// display scale it picks for itself: 1.4 in one process and 1.5 in another on the same
    /// 140%-scaled machine, unaffected by the process's DPI awareness. So the scale is measured
    /// with a tiny probe render, the destination is divided by it, rounded up so the render is
    /// never short, and the overshoot (a pixel or so) is cropped off the right and bottom edges.
    /// </summary>
    private static async Task<SoftwareBitmap> RenderExactAsync(
        PdfPage page, PageRenderSettings settings, CancellationToken cancellationToken)
    {
        (uint width, uint height) = PixelSize(page.Size, settings.Dpi);
        double renderScale = await MeasureRenderScaleAsync(page, ExactScaleProbeWidth, cancellationToken);
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Ceiling(width / renderScale),
            DestinationHeight = (uint)Math.Ceiling(height / renderScale),
        };
        if (settings.TransparentBackground)
        {
            options.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }

        using var rendered = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(rendered, options).AsTask(cancellationToken);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(rendered).AsTask(cancellationToken);
        if (decoder.PixelWidth < width || decoder.PixelHeight < height)
        {
            throw new InvalidOperationException(
                $"Render came out {decoder.PixelWidth}x{decoder.PixelHeight}, short of {width}x{height}: " +
                $"the renderer did not scale by the probed {renderScale}.");
        }

        var crop = new BitmapTransform
        {
            Rotation = ToBitmapRotation(settings.RotationDegrees),
            Bounds = CropBounds(decoder.PixelWidth, decoder.PixelHeight, width, height, settings.RotationDegrees),
        };
        BitmapAlphaMode alphaMode = settings.TransparentBackground ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore;
        SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, alphaMode, crop, ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
        bitmap.DpiX = settings.Dpi;
        bitmap.DpiY = settings.Dpi;
        return bitmap;
    }

    private static async Task<double> MeasureRenderScaleAsync(
        PdfPage page, uint probeWidth, CancellationToken cancellationToken)
    {
        using var probe = new InMemoryRandomAccessStream();
        var options = new PdfPageRenderOptions { DestinationWidth = probeWidth, DestinationHeight = 1 };
        await page.RenderToStreamAsync(probe, options).AsTask(cancellationToken);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(probe).AsTask(cancellationToken);
        return (double)decoder.PixelWidth / probeWidth;
    }

    /// <summary>
    /// BitmapTransform rotates before it crops, so the bounds are in rotated coordinates. The
    /// render's overshoot sits on the unrotated right and bottom edges; wherever the rotation
    /// carries those edges, the crop is offset to cut them off there rather than cutting real
    /// page off the far side.
    /// </summary>
    internal static BitmapBounds CropBounds(
        uint renderedWidth, uint renderedHeight, uint width, uint height, int rotationDegrees) => rotationDegrees switch
    {
        0 => new BitmapBounds { Width = width, Height = height },
        90 => new BitmapBounds { X = renderedHeight - height, Width = height, Height = width },
        180 => new BitmapBounds { X = renderedWidth - width, Y = renderedHeight - height, Width = width, Height = height },
        270 => new BitmapBounds { Y = renderedWidth - width, Width = height, Height = width },
        _ => throw new ArgumentOutOfRangeException(nameof(rotationDegrees), rotationDegrees, null),
    };

    private static BitmapRotation ToBitmapRotation(int rotationDegrees) => rotationDegrees switch
    {
        0 => BitmapRotation.None,
        90 => BitmapRotation.Clockwise90Degrees,
        180 => BitmapRotation.Clockwise180Degrees,
        270 => BitmapRotation.Clockwise270Degrees,
        _ => throw new ArgumentOutOfRangeException(nameof(rotationDegrees), rotationDegrees, null),
    };

    // Internal so a test can reach the delete-on-failure path: through RenderToFileAsync, a
    // cancelled token stops at the gate before any file exists. The stream must stay inside the
    // try, so it is closed before the delete; hoisted out, the delete hits a sharing violation.
    internal static async Task WriteEncodedAsync(
        SoftwareBitmap bitmap, PageRenderSettings settings, string outputPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
            using IRandomAccessStream output = fileStream.AsRandomAccessStream();
            await EncodeAsync(bitmap, settings, output, cancellationToken);
        }
        catch
        {
            File.Delete(outputPath);
            throw;
        }
    }

    private static async Task EncodeAsync(
        SoftwareBitmap bitmap, PageRenderSettings settings, IRandomAccessStream output, CancellationToken cancellationToken)
    {
        BitmapEncoder encoder = settings.Format == PageImageFormat.Jpeg
            ? await BitmapEncoder.CreateAsync(settings.Format.EncoderId(), output, JpegOptions(settings.JpegQuality))
            : await BitmapEncoder.CreateAsync(settings.Format.EncoderId(), output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(cancellationToken);
    }

    private static BitmapPropertySet JpegOptions(int quality) => new()
    {
        ["ImageQuality"] = new BitmapTypedValue(quality / 100f, PropertyType.Single),
    };

    private static uint ScaleToPixels(double viewUnits, int dpi) =>
        (uint)Math.Max(1, Math.Round(viewUnits * dpi / 96.0));

    private uint PageIndex(int pageNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageNumber, PageCount);
        return (uint)(pageNumber - 1);
    }
}
