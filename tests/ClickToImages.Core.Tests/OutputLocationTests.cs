using ClickToImages.Core.Export;

namespace ClickToImages.Core.Tests;

public class OutputLocationTests
{
    private const string Pdf = @"C:\docs\scans\report.pdf";

    [Theory]
    [InlineData(OutputLocation.SubfolderNamedAfterPdf, @"C:\docs\scans\report")]
    [InlineData(OutputLocation.BesidePdf, @"C:\docs\scans")]
    [InlineData(OutputLocation.ChosenFolder, @"D:\exports")]
    public void FolderFor_ResolvesEachLocation(OutputLocation location, string expected)
    {
        Assert.Equal(expected, location.FolderFor(Pdf, @"D:\exports"));
    }

    [Fact]
    public void FolderFor_ChosenFolderWithoutOne_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OutputLocation.ChosenFolder.FolderFor(Pdf, chosenFolder: null));
    }
}
