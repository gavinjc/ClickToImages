using Windows.Graphics.Imaging;

namespace ClickToImages.Core.Rendering;

public enum PageImageFormat
{
    Png,
    Jpeg,
    Tiff,
}

public static class PageImageFormats
{
    // The OS's own WIC encoders. Not passed as PdfPageRenderOptions.BitmapEncoderId, which can set
    // neither the DPI written into the file nor JPEG quality; see PdfPageRenderer.RenderExactAsync.
    public static Guid EncoderId(this PageImageFormat format) => format switch
    {
        PageImageFormat.Png => BitmapEncoder.PngEncoderId,
        PageImageFormat.Jpeg => BitmapEncoder.JpegEncoderId,
        PageImageFormat.Tiff => BitmapEncoder.TiffEncoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static string FileExtension(this PageImageFormat format) => format switch
    {
        PageImageFormat.Png => ".png",
        PageImageFormat.Jpeg => ".jpg",
        PageImageFormat.Tiff => ".tif",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static bool SupportsTransparency(this PageImageFormat format) => format == PageImageFormat.Png;
}
