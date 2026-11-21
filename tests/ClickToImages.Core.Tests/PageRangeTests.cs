using ClickToImages.Core.Rendering;

namespace ClickToImages.Core.Tests;

public class PageRangeTests
{
    [Fact]
    public void Parse_MixedSinglesRangesAndOpenEnd_ReturnsPagesInDocumentOrder()
    {
        Assert.Equal([1, 2, 3, 4, 7, 12, 13, 14], PageRange.Parse("1-4, 7, 12-", 14));
    }

    [Fact]
    public void Parse_OverlappingAndUnorderedParts_ReturnsEachPageOnceSorted()
    {
        Assert.Equal([2, 3, 4, 5, 9], PageRange.Parse("9, 3-5, 2-4", 10));
    }

    [Fact]
    public void Parse_SpacesAroundDashAndEmptyParts_AreTolerated()
    {
        Assert.Equal([2, 3, 6], PageRange.Parse(" 2 - 3 ,, 6 ,", 6));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("3-11")]
    [InlineData("5-2")]
    [InlineData("a")]
    [InlineData("-3")]
    [InlineData("1-2-3")]
    [InlineData("+2")]
    public void Parse_InvalidOrOutOfRange_Throws(string text)
    {
        Assert.Throws<FormatException>(() => PageRange.Parse(text, 10));
    }
}
