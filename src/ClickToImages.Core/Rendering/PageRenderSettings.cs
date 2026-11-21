namespace ClickToImages.Core.Rendering;

public sealed record PageRenderSettings
{
    // 96 is the API's own default and renders text like a fax (spec §4.2); never fall back to it.
    public const int DefaultDpi = 150;

    // What WIC's JPEG encoder uses when not told.
    public const int DefaultJpegQuality = 90;

    private readonly int dpi = DefaultDpi;
    private readonly int jpegQuality = DefaultJpegQuality;
    private readonly int rotationDegrees;

    public int Dpi
    {
        get => dpi;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(Dpi));
            dpi = value;
        }
    }

    public PageImageFormat Format { get; init; } = PageImageFormat.Png;

    public bool TransparentBackground { get; init; }

    // Clockwise, applied to the image written; the source PDF is never touched (spec §7).
    public int RotationDegrees
    {
        get => rotationDegrees;
        init
        {
            if (value is not (0 or 90 or 180 or 270))
            {
                throw new ArgumentOutOfRangeException(nameof(RotationDegrees), value, "Must be 0, 90, 180 or 270.");
            }

            rotationDegrees = value;
        }
    }

    // 1-100; ignored for PNG and TIFF.
    public int JpegQuality
    {
        get => jpegQuality;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(JpegQuality));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100, nameof(JpegQuality));
            jpegQuality = value;
        }
    }
}
