using ClickToImages.App.State;

namespace ClickToImages.App.Tests;

public class WindowTitleTests
{
    private const string AppTitle = "ClickTo: Images";
    private const string WithFolderFormat = "{0} — {1}";

    [Fact]
    public void Format_NoFolder_IsAppTitleAlone()
    {
        Assert.Equal(AppTitle, WindowTitle.Format(null, AppTitle, WithFolderFormat));
    }

    [Fact]
    public void Format_Folder_LeadsWithItsName()
    {
        Assert.Equal("Holiday — ClickTo: Images", WindowTitle.Format(@"C:\Scans\Holiday", AppTitle, WithFolderFormat));
    }

    [Fact]
    public void Format_TrailingSeparator_StillUsesFolderName()
    {
        Assert.Equal("Holiday — ClickTo: Images", WindowTitle.Format(@"C:\Scans\Holiday\", AppTitle, WithFolderFormat));
    }

    // Path.GetFileName returns "" for a root, which would otherwise give " — ClickTo: Images".
    [Fact]
    public void Format_DriveRoot_UsesThePathItself()
    {
        Assert.Equal(@"D:\ — ClickTo: Images", WindowTitle.Format(@"D:\", AppTitle, WithFolderFormat));
    }
}
