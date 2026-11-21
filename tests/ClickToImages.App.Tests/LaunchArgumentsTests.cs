using System.Text;
using ClickToImages.App.State;

namespace ClickToImages.App.Tests;

public class LaunchArgumentsTests
{
    [Fact]
    public void Parse_ListAndSkipped_TakesBothPathsAndNoPdfs()
    {
        LaunchArguments launch = LaunchArguments.Parse(["--list", @"C:\t\a.list", "--skipped", @"C:\t\a.skipped"]);

        Assert.Equal(@"C:\t\a.list", launch.ListPath);
        Assert.Equal(@"C:\t\a.skipped", launch.SkippedPath);
        Assert.Empty(launch.PdfPaths);
    }

    [Fact]
    public void Parse_ListOnly_HasNoSkippedPath()
    {
        LaunchArguments launch = LaunchArguments.Parse(["--list", @"C:\t\a.list"]);

        Assert.Equal(@"C:\t\a.list", launch.ListPath);
        Assert.Null(launch.SkippedPath);
    }

    [Fact]
    public void Parse_BarePaths_AreDevLaunchPdfs()
    {
        LaunchArguments launch = LaunchArguments.Parse([@"C:\a.pdf", @"C:\b.pdf"]);

        Assert.Null(launch.ListPath);
        Assert.Equal([@"C:\a.pdf", @"C:\b.pdf"], launch.PdfPaths);
    }

    [Fact]
    public void Parse_ListWithoutValue_IsNotTakenAsAPdfPath()
    {
        LaunchArguments launch = LaunchArguments.Parse(["--list"]);

        Assert.Null(launch.ListPath);
        Assert.Empty(launch.PdfPaths);
    }

    // "--list --skipped x" must not read "--skipped" as the list file's path.
    [Fact]
    public void Parse_ListFollowedBySwitch_HasNoListPath()
    {
        LaunchArguments launch = LaunchArguments.Parse(["--list", "--skipped", @"C:\t\a.skipped"]);

        Assert.Null(launch.ListPath);
        Assert.Equal(@"C:\t\a.skipped", launch.SkippedPath);
    }

    [Fact]
    public void Parse_SkippedWithoutList_IsStillAShellLaunch()
    {
        LaunchArguments launch = LaunchArguments.Parse(["--skipped", @"C:\t\a.skipped"]);

        Assert.Null(launch.ListPath);
        Assert.Equal(@"C:\t\a.skipped", launch.SkippedPath);
        Assert.Empty(launch.PdfPaths);
    }

    [Fact]
    public void ReadAndDeleteTempFile_ReturnsUtf8LinesAndDeletesTheFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.list");
        File.WriteAllText(path, "C:\\Scans\\Été\\a.pdf\nC:\\Scans\\b.pdf\n", new UTF8Encoding(false));

        string[] lines = LaunchArguments.ReadAndDeleteTempFile(path);

        Assert.Equal([@"C:\Scans\Été\a.pdf", @"C:\Scans\b.pdf"], lines);
        Assert.False(File.Exists(path));
    }
}
