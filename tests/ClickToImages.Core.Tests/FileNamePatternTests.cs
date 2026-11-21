using ClickToImages.Core.Export;
using ClickToImages.Core.Rendering;

namespace ClickToImages.Core.Tests;

public class FileNamePatternTests
{
    private const string DocumentName = "report";

    [Theory]
    [InlineData(7, 9, PageImageFormat.Png, "report-p7.png")]
    [InlineData(7, 12, PageImageFormat.Jpeg, "report-p07.jpg")]
    [InlineData(7, 200, PageImageFormat.Tiff, "report-p007.tif")]
    [InlineData(200, 200, PageImageFormat.Png, "report-p200.png")]
    public void Default_PadsPageToWidthOfPageCount(int pageNumber, int pageCount, PageImageFormat format, string expected)
    {
        var pattern = new FileNamePattern(FileNamePattern.Default);

        Assert.Equal(expected, pattern.FileName(DocumentName,pageNumber, pageCount, 150, format));
    }

    [Fact]
    public void FileName_FillsEveryToken_CaseInsensitively()
    {
        var pattern = new FileNamePattern("{NAME} page {Page} of {total} @{dpi}dpi");

        Assert.Equal("report page 03 of 12 @300dpi.png", pattern.FileName(DocumentName,3, 12, 300, PageImageFormat.Png));
    }

    [Fact]
    public void FileName_KeepsDotsInsideTheDocumentName()
    {
        var pattern = new FileNamePattern(FileNamePattern.Default);

        Assert.Equal("v1.2 notes-p1.png", pattern.FileName("v1.2 notes", 1, 1, 150, PageImageFormat.Png));
    }

    [Theory]
    [InlineData("{name}-p{page}", true, true)]
    [InlineData("{NAME} {Page}", true, true)]
    [InlineData("{name}", false, true)]
    [InlineData("scan {page} of {total}", true, false)]
    public void NumbersPagesAndNamesDocument_FollowTheTokens(string text, bool numbersPages, bool namesDocument)
    {
        var pattern = new FileNamePattern(text);

        Assert.Equal((numbersPages, namesDocument), (pattern.NumbersPages, pattern.NamesDocument));
    }

    [Theory]
    [InlineData("", FileNamePatternProblem.Empty)]
    [InlineData("   ", FileNamePatternProblem.Empty)]
    [InlineData("{name}-{pages}", FileNamePatternProblem.UnknownToken)]
    [InlineData("{}", FileNamePatternProblem.UnknownToken)]
    [InlineData("{name}-{page", FileNamePatternProblem.UnclosedBrace)]
    [InlineData("{name}/{page}", FileNamePatternProblem.InvalidCharacter)]
    [InlineData("{name}:{page}", FileNamePatternProblem.InvalidCharacter)]
    [InlineData("page?{page}", FileNamePatternProblem.InvalidCharacter)]
    public void FindProblem_NamesWhatIsWrong(string text, FileNamePatternProblem expected)
    {
        Assert.Equal(expected, FileNamePattern.FindProblem(text));
    }

    [Theory]
    [InlineData(FileNamePattern.Default)]
    [InlineData("scan")]
    [InlineData("{page}}")]
    public void FindProblem_AcceptsUsablePatterns(string text)
    {
        Assert.Null(FileNamePattern.FindProblem(text));
    }

    [Fact]
    public void Constructor_RefusesAnUnusablePattern()
    {
        Assert.Throws<FormatException>(() => new FileNamePattern("{name}/{page}"));
    }
}
