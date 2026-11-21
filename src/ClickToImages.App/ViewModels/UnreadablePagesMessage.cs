namespace ClickToImages.App.ViewModels;

// The warning after a save that left out ticked pages which couldn't be read: separate strings for
// one page and for several, never "(s)" (Bureau.Common app-conventions.md). getString is the .resw
// lookup, passed in because ResourceLoader throws under the test host.
public static class UnreadablePagesMessage
{
    // pageNames are already formatted as "report.pdf p4", in grid order.
    public static string Format(IReadOnlyList<string> pageNames, Func<string, string> getString) =>
        pageNames.Count == 1
            ? string.Format(getString("UnreadablePagesOneFormat"), pageNames[0])
            : string.Format(getString("UnreadablePagesManyFormat"), pageNames.Count, string.Join(", ", pageNames));
}
