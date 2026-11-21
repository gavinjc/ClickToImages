using ClickToImages.Core.Rendering;

namespace ClickToImages.Core.Tests;

public class PageRenderSettingsTests
{
    [Fact]
    public void Defaults_Are150DpiPngQuality90Opaque()
    {
        var settings = new PageRenderSettings();

        Assert.Equal((150, PageImageFormat.Png, 90, false),
            (settings.Dpi, settings.Format, settings.JpegQuality, settings.TransparentBackground));
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(-150, 90)]
    [InlineData(150, 0)]
    [InlineData(150, 101)]
    public void OutOfRangeDpiOrQuality_ThrowsOnConstruction(int dpi, int quality)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRenderSettings { Dpi = dpi, JpegQuality = quality });
    }

    [Theory]
    [InlineData(-90)]
    [InlineData(45)]
    [InlineData(360)]
    public void RotationOtherThanAQuarterTurnStep_ThrowsOnConstruction(int degrees)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRenderSettings { RotationDegrees = degrees });
    }
}
