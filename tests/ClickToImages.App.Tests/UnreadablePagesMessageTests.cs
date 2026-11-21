using ClickToImages.App.ViewModels;

namespace ClickToImages.App.Tests;

// Formats with the .resw's own values, so a wording change there shows up here and a key the
// formatter asks for that the .resw lacks throws.
public class UnreadablePagesMessageTests
{
    private static string GetString(string key) => ResourceKeyTests.ReswValues[key];

    [Fact]
    public void OnePage_UsesTheSingularWording()
    {
        Assert.Equal(
            "A ticked page couldn't be read and wasn't saved: report.pdf p4",
            UnreadablePagesMessage.Format(["report.pdf p4"], GetString));
    }

    [Fact]
    public void SeveralPages_CountsAndListsThemInOrder()
    {
        Assert.Equal(
            "3 ticked pages couldn't be read and weren't saved: report.pdf p4, report.pdf p9, scan.pdf p1",
            UnreadablePagesMessage.Format(["report.pdf p4", "report.pdf p9", "scan.pdf p1"], GetString));
    }
}
