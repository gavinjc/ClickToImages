namespace ClickToImages.App.ViewModels;

// The skipped row's text: "Skipped notes.txt" for one item, "3 files skipped: a.txt, ..." for
// several, never "(s)" (Bureau.Common app-conventions.md). getString is the .resw lookup, passed
// in because ResourceLoader throws under the test host.
public static class SkippedMessage
{
    // otherNames are files skipped by type (non-PDFs, unreadable folders), listed before the PDFs.
    public static string Format(
        IReadOnlyList<string> otherNames, IReadOnlyList<SkippedDocument> documents, Func<string, string> getString)
    {
        int count = otherNames.Count + documents.Count;
        if (count == 1)
        {
            return documents is [{ Reason: SkipReason.AlreadyAdded } duplicate]
                ? string.Format(getString("SkippedAlreadyAddedOneFormat"), duplicate.FileName)
                : string.Format(getString("SkippedOneFormat"), otherNames.Count == 1
                    ? otherNames[0]
                    : Describe(documents[0], getString));
        }

        IEnumerable<string> names = otherNames.Concat(documents.Select(document => Describe(document, getString)));
        return string.Format(getString("SkippedManyFormat"), count, string.Join(", ", names));
    }

    // The PDF's name with its reason, as it reads in the list and after "Skipped" alike.
    private static string Describe(SkippedDocument document, Func<string, string> getString) => string.Format(
        getString(document.Reason switch
        {
            SkipReason.NoPasswordGiven => "SkippedNoPasswordFormat",
            SkipReason.WrongPassword => "SkippedWrongPasswordFormat",
            SkipReason.AlreadyAdded => "SkippedAlreadyAddedFormat",
            _ => "SkippedUnsupportedEncryptionFormat",
        }),
        document.FileName);
}
