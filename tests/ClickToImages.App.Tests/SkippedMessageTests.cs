using ClickToImages.App.ViewModels;

namespace ClickToImages.App.Tests;

// Formats with the .resw's own values, so a wording change there shows up here and a key the
// formatter asks for that the .resw lacks throws.
public class SkippedMessageTests
{
    private static string GetString(string key) => ResourceKeyTests.ReswValues[key];

    [Fact]
    public void OnePlainSkip_ReadsSkippedAndTheName()
    {
        Assert.Equal("Skipped notes.txt", SkippedMessage.Format(["notes.txt"], [], GetString));
    }

    [Fact]
    public void OneDuplicate_PutsAlreadyAddedAfterTheName()
    {
        string message = SkippedMessage.Format([], [new("report.pdf", SkipReason.AlreadyAdded)], GetString);

        Assert.Equal("Skipped report.pdf: already added", message);
    }

    [Fact]
    public void OnePasswordSkip_KeepsItsReasonInBrackets()
    {
        string message = SkippedMessage.Format([], [new("locked.pdf", SkipReason.WrongPassword)], GetString);

        Assert.Equal("Skipped locked.pdf (wrong password)", message);
    }

    [Fact]
    public void SeveralMixed_CountsAllAndListsOtherFilesFirst()
    {
        string message = SkippedMessage.Format(
            ["a.txt"],
            [new("b.pdf", SkipReason.AlreadyAdded), new("locked.pdf", SkipReason.WrongPassword)],
            GetString);

        Assert.Equal("3 files skipped: a.txt, already added: b.pdf, locked.pdf (wrong password)", message);
    }

    [Fact]
    public void TwoPlainSkips_UseTheManyWording()
    {
        Assert.Equal("2 files skipped: a.txt, b.png", SkippedMessage.Format(["a.txt", "b.png"], [], GetString));
    }

    public static TheoryData<string[], SkippedDocument[]> EveryShape => new()
    {
        { ["notes.txt"], [] },
        { [], [new("report.pdf", SkipReason.AlreadyAdded)] },
        { [], [new("locked.pdf", SkipReason.NoPasswordGiven)] },
        { [], [new("locked.pdf", SkipReason.WrongPassword)] },
        { [], [new("aes.pdf", SkipReason.UnsupportedEncryption)] },
        { ["a.txt", "b.png"], [new("aes.pdf", SkipReason.UnsupportedEncryption)] },
    };

    [Theory]
    [MemberData(nameof(EveryShape))]
    public void NoMessage_SaysItemsInBrackets(string[] otherNames, SkippedDocument[] documents)
    {
        Assert.DoesNotContain("(s)", SkippedMessage.Format(otherNames, documents, GetString));
    }
}
