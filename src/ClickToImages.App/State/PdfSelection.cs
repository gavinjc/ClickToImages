using ClickToImages.Core;

namespace ClickToImages.App.State;

// What a drop or the dev command line hands the app, sorted the way the shell extension sorts a
// right-click selection (ExplorerCommand.cpp, Invoke and ExpandFolder), so every route agrees:
// - a PDF is a PDF;
// - a folder gives the PDFs directly inside it, and its other files are skipped; subfolders are
//   ignored, not skipped;
// - a folder with no PDF directly inside is skipped by its own name;
// - anything else is skipped.
public sealed record PdfSelection(IReadOnlyList<string> PdfPaths, IReadOnlyList<string> SkippedNames)
{
    public static PdfSelection Expand(IEnumerable<string> paths)
    {
        List<string> pdfPaths = [];
        List<string> skippedNames = [];
        foreach (string path in paths)
        {
            if (SupportedExtensions.Includes(path))
            {
                pdfPaths.Add(path);
            }
            else if (Directory.Exists(path))
            {
                ExpandFolder(path, pdfPaths, skippedNames);
            }
            else
            {
                skippedNames.Add(Path.GetFileName(path));
            }
        }

        return new PdfSelection(pdfPaths, skippedNames);
    }

    // A folder that can't be listed is skipped by name, as one with no PDF is; the shell extension
    // reads it with an error_code and ends up in the same place.
    private static void ExpandFolder(string folder, List<string> pdfPaths, List<string> skippedNames)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [];
        }

        List<string> pdfs = files.Where(SupportedExtensions.Includes).ToList();
        if (pdfs.Count == 0)
        {
            skippedNames.Add(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)));
            return;
        }

        pdfPaths.AddRange(pdfs);
        skippedNames.AddRange(files.Where(file => !SupportedExtensions.Includes(file)).Select(Path.GetFileName)!);
    }
}
