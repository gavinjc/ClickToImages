using ClickToImages.Core.Rendering;

namespace ClickToImages.Core.Export;

// RotationDegrees is clockwise, applied to the image only.
public sealed record PageExport(PdfPageRenderer Renderer, int PageNumber, int RotationDegrees, string OutputPath);

public enum NameClashFix
{
    AddPage,
    AddName,
    Reword,
}

public sealed record NameClash(string OutputPath, NameClashFix Fix);

public static class PageExporter
{
    /// <summary>
    /// Renders every page to a temp file beside its target, and only once all of them have
    /// rendered moves each into place. So a cancelled or failed export leaves no partial output
    /// (spec §5) and every file the user had as it was: the temps are deleted, any file already
    /// replaced is put back, and any output folder this export created that is left empty is
    /// removed. Pages render one at a time, which is all spec §5's memory budget allows at 600 DPI.
    /// Existing files at the targets are replaced on success; asking first is the caller's job.
    /// </summary>
    /// <param name="progress">The number of pages rendered so far, after each page.</param>
    public static async Task ExportAsync(
        IReadOnlyList<PageExport> pages, PageRenderSettings settings, IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (FirstSharedOutputPath(pages) is { } shared)
        {
            throw new ArgumentException($"Two pages would both be saved as {shared.Path}.", nameof(pages));
        }

        List<string> createdFolders = [];
        var tempPaths = new List<string>(pages.Count);
        try
        {
            foreach (string folder in pages.Select(page => Path.GetDirectoryName(page.OutputPath)!).Distinct())
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    createdFolders.Add(folder);
                }
            }

            foreach (PageExport page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempPath = $"{page.OutputPath}.{Guid.NewGuid():N}.tmp";
                tempPaths.Add(tempPath);
                await page.Renderer.RenderToFileAsync(
                    page.PageNumber, settings with { RotationDegrees = page.RotationDegrees }, tempPath,
                    cancellationToken);
                progress?.Report(tempPaths.Count);
            }

            // Not cancellable: a few renames, and stopping halfway would leave a mix of old and new.
            MoveIntoPlace(pages, tempPaths);
        }
        catch
        {
            DeleteLeftovers(tempPaths, createdFolders);
            throw;
        }
    }

    // Each existing target is moved aside before its temp takes its place, so when any rename
    // fails (a target open in an image viewer that doesn't share delete, say) every file already
    // replaced can be put back. A target that can't be moved aside fails before it is touched.
    private static void MoveIntoPlace(IReadOnlyList<PageExport> pages, List<string> tempPaths)
    {
        var placed = new List<(string Target, string? Backup)>(pages.Count);
        try
        {
            for (int i = 0; i < pages.Count; i++)
            {
                string target = pages[i].OutputPath;
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = $"{target}.{Guid.NewGuid():N}.bak";
                    File.Move(target, backup);
                }

                // Recorded before the temp moves, so a failure of that move still restores the backup.
                placed.Add((target, backup));
                File.Move(tempPaths[i], target);
            }
        }
        catch
        {
            foreach ((string target, string? backup) in placed)
            {
                TryDelete(() =>
                {
                    File.Delete(target);
                    if (backup is not null)
                    {
                        File.Move(backup, target);
                    }
                });
            }

            throw;
        }

        foreach ((_, string? backup) in placed)
        {
            if (backup is not null)
            {
                TryDelete(() => File.Delete(backup));
            }
        }
    }

    // The one definition of "same output path", for FindNameClash's advice and ExportAsync's guard
    // alike. Case-insensitive, as NTFS is: report-P1.png and report-p1.png are one file.
    private static (string Path, PageExport First, PageExport Second)? FirstSharedOutputPath(
        IReadOnlyList<PageExport> pages)
    {
        var firstByPath = new Dictionary<string, PageExport>(StringComparer.OrdinalIgnoreCase);
        foreach (PageExport page in pages)
        {
            string path = Path.GetFullPath(page.OutputPath);
            if (!firstByPath.TryAdd(path, page))
            {
                return (path, firstByPath[path], page);
            }
        }

        return null;
    }

    /// <summary>
    /// The first output path two of <paramref name="pages"/> share, named by
    /// <paramref name="pattern"/>, and the change to the pattern that would end it. Names from
    /// <see cref="DocumentNames.Distinguish"/> never clash across PDFs while the pattern has
    /// <c>{name}</c>, nor within a PDF while it has <c>{page}</c>; what is left is a pattern whose
    /// tokens run together, which only rewording fixes.
    /// </summary>
    public static NameClash? FindNameClash(IReadOnlyList<PageExport> pages, FileNamePattern pattern)
    {
        if (FirstSharedOutputPath(pages) is not { } shared)
        {
            return null;
        }

        // A PDF has one renderer, so sharing it means sharing the PDF.
        bool samePdf = ReferenceEquals(shared.First.Renderer, shared.Second.Renderer);
        NameClashFix fix = (samePdf, pattern) switch
        {
            (true, { NumbersPages: false }) => NameClashFix.AddPage,
            (false, { NamesDocument: false }) => NameClashFix.AddName,
            _ => NameClashFix.Reword,
        };
        return new NameClash(shared.Path, fix);
    }

    // A temp that was already moved is simply gone (File.Delete ignores a missing file), so this is
    // safe after a failed move too. A leftover that won't delete or restore (a virus scanner
    // holding it, say) is left rather than thrown, so the export's own failure is the one the
    // caller sees.
    private static void DeleteLeftovers(List<string> tempPaths, List<string> createdFolders)
    {
        foreach (string tempPath in tempPaths)
        {
            TryDelete(() => File.Delete(tempPath));
        }

        foreach (string folder in createdFolders)
        {
            TryDelete(() =>
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            });
        }
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
