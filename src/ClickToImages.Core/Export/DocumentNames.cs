namespace ClickToImages.Core.Export;

public static class DocumentNames
{
    /// <summary>
    /// The <c>{name}</c> each PDF's images get, in the order given. Two PDFs with one file name
    /// (report.pdf from two folders) whose images go to the same folder would otherwise clash, and
    /// no pattern could tell them apart, so the second gets " (2)", the third " (3)", and so on, as
    /// Explorer names copies. A number another of these PDFs' own name already holds (a real
    /// "report (2).pdf") is skipped. PDFs going to different folders keep their plain names.
    /// </summary>
    public static IReadOnlyList<string> Distinguish(IReadOnlyList<(string PdfPath, string OutputFolder)> documents)
    {
        // Paths rather than bare names, so only names in the same folder count; case-insensitive,
        // as NTFS is.
        var ownNames = documents
            .Select(document => Path.Combine(document.OutputFolder, Path.GetFileNameWithoutExtension(document.PdfPath)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>(documents.Count);
        foreach ((string pdfPath, string folder) in documents)
        {
            string plainName = Path.GetFileNameWithoutExtension(pdfPath);
            string name = plainName;
            int copy = 1;
            while (!taken.Add(Path.Combine(folder, name)))
            {
                do
                {
                    copy++;
                    name = $"{plainName} ({copy})";
                }
                while (ownNames.Contains(Path.Combine(folder, name)));
            }

            names.Add(name);
        }

        return names;
    }
}
