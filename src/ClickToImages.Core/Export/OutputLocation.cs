namespace ClickToImages.Core.Export;

public enum OutputLocation
{
    // The default: exporting 120 pages loose into someone's Documents folder is hostile (spec §4.3).
    SubfolderNamedAfterPdf,
    BesidePdf,
    ChosenFolder,
}

public static class OutputLocations
{
    public static string FolderFor(this OutputLocation location, string pdfPath, string? chosenFolder)
    {
        string fullPath = Path.GetFullPath(pdfPath);
        string pdfFolder = Path.GetDirectoryName(fullPath)!;
        return location switch
        {
            OutputLocation.SubfolderNamedAfterPdf => Path.Combine(pdfFolder, Path.GetFileNameWithoutExtension(fullPath)),
            OutputLocation.BesidePdf => pdfFolder,
            OutputLocation.ChosenFolder => chosenFolder
                ?? throw new ArgumentNullException(nameof(chosenFolder), "No folder has been chosen."),
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
        };
    }
}
