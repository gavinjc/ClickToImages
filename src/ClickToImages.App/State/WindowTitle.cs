namespace ClickToImages.App.State;

// Kept apart from MainWindow so it can be unit-tested: MainWindow's static ResourceLoader throws
// under the test host. Every right-click opens a new window, so the folder name is what tells
// them apart (app-conventions.md → Behaviour).
public static class WindowTitle
{
    // Folder first, so it survives the taskbar truncating a long title.
    public static string Format(string? folderPath, string appTitle, string withFolderFormat)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            return appTitle;
        }

        // GetFileName returns "" for a drive root such as D:\, so fall back to the path itself.
        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
        return string.Format(withFolderFormat, folderName.Length > 0 ? folderName : folderPath, appTitle);
    }
}
