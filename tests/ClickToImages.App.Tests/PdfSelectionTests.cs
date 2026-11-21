using ClickToImages.App.State;

namespace ClickToImages.App.Tests;

// Matches the shell extension's folder rule, so a dropped folder gives what right-clicking it would.
public sealed class PdfSelectionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ClickToImagesSelection").FullName;

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Folder_GivesOnlyThePdfsDirectlyInside_SkipsItsOtherFiles_AndIgnoresSubfolders()
    {
        string scans = Touch("scans", "b.pdf", "a.PDF", "notes.txt");
        Touch(Path.Combine("scans", "older"), "deep.pdf", "deep.txt");

        PdfSelection selection = PdfSelection.Expand([scans]);

        Assert.Equal(
            [Path.Combine(scans, "a.PDF"), Path.Combine(scans, "b.pdf")],
            selection.PdfPaths.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["notes.txt"], selection.SkippedNames);
    }

    [Fact]
    public void FolderWithNoPdfDirectlyInside_IsSkippedByItsOwnName()
    {
        string photos = Touch("photos", "cat.jpg");
        Touch(Path.Combine("photos", "inner"), "only-here.pdf");

        PdfSelection selection = PdfSelection.Expand([photos]);

        Assert.Empty(selection.PdfPaths);
        Assert.Equal(["photos"], selection.SkippedNames);
    }

    // A folder path ending in \ would otherwise be skipped with a blank name.
    [Fact]
    public void FolderWithTrailingSeparator_IsSkippedByItsOwnName()
    {
        string photos = Touch("photos", "cat.jpg");

        PdfSelection selection = PdfSelection.Expand([photos + Path.DirectorySeparatorChar]);

        Assert.Equal(["photos"], selection.SkippedNames);
    }

    [Fact]
    public void LooseFiles_PdfsKept_OthersSkipped_InTheOrderGiven()
    {
        string folder = Touch("loose", "one.pdf", "two.docx");

        PdfSelection selection = PdfSelection.Expand([Path.Combine(folder, "two.docx"), Path.Combine(folder, "one.pdf")]);

        Assert.Equal([Path.Combine(folder, "one.pdf")], selection.PdfPaths);
        Assert.Equal(["two.docx"], selection.SkippedNames);
    }

    private string Touch(string relativeFolder, params string[] fileNames)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_folder, relativeFolder)).FullName;
        foreach (string name in fileNames)
        {
            File.WriteAllText(Path.Combine(folder, name), "");
        }

        return folder;
    }
}
