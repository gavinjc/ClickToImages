using ClickToImages.App.State;
using ClickToImages.Core.Export;
using ClickToImages.Core.Rendering;

namespace ClickToImages.App.Tests;

public class ExportOptionsTests
{
    [Fact]
    public void Defaults_AreGoodPngIntoASubfolderNamedAfterThePdf()
    {
        var options = new ExportOptions();

        Assert.Equal(150, options.RenderSettings.Dpi);
        Assert.Equal(@"C:\docs\report", options.OutputFolder(@"C:\docs\report.pdf"));
        Assert.Equal("report-p07.png", options.FileName("report", 7, 12));
    }

    [Theory]
    [InlineData(PageImageFormat.Png, true)]
    [InlineData(PageImageFormat.Jpeg, false)]
    [InlineData(PageImageFormat.Tiff, false)]
    public void RenderSettings_ApplyTransparencyToPngOnly(PageImageFormat format, bool expected)
    {
        var options = new ExportOptions { Format = format, TransparentBackground = true };

        Assert.Equal(expected, options.RenderSettings.TransparentBackground);
    }

    [Fact]
    public void FileName_UsesThePatternPresetDpiAndFormat()
    {
        var options = new ExportOptions
        {
            Resolution = ResolutionPreset.Print,
            Format = PageImageFormat.Jpeg,
            NamingPattern = "{name} {page}of{total} {dpi}",
        };

        Assert.Equal("report 3of9 300.jpg", options.FileName("report", 3, 9));
    }
}
