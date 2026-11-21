namespace ClickToImages.App;

// Kept out of MainWindow so the tests can read it: touching MainWindow runs its static
// ResourceLoader, which throws under the test host.
public static class HelpTopicKeys
{
    // Each key needs "{key}Title" and "{key}Body" in Strings/en-us/Resources.resw.
    public static readonly IReadOnlyList<string> InOrder =
    [
        "HelpGettingStarted",
        "HelpChoosingPages",
        "HelpRotatePreview",
        "HelpResolution",
        "HelpFormats",
        "HelpWhereImagesGo",
        "HelpFileNames",
        "HelpCopyPage",
        "HelpCancelling",
        "HelpPasswords",
        "HelpSkippedFiles",
        "HelpPrivacy",
        "HelpKeyboardShortcuts",
    ];
}
