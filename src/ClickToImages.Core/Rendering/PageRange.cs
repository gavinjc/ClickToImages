namespace ClickToImages.Core.Rendering;

public static class PageRange
{
    /// <summary>
    /// Parses the usual print-dialog form, e.g. <c>1-4, 7, 12-</c>, into 1-based page numbers in
    /// document order without repeats. A trailing <c>N-</c> runs to the last page. A page outside
    /// 1..<paramref name="pageCount"/> is an error rather than being clamped, so a typo like
    /// <c>1-400</c> on a 40-page document is caught, not silently accepted.
    /// </summary>
    /// <exception cref="FormatException">The text is empty, malformed or out of range.</exception>
    public static IReadOnlyList<int> Parse(string text, int pageCount)
    {
        var pages = new SortedSet<int>();
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new FormatException("No pages given.");
        }

        foreach (string part in parts)
        {
            (int first, int last) = ParsePart(part, pageCount);
            for (int page = first; page <= last; page++)
            {
                pages.Add(page);
            }
        }

        return [.. pages];
    }

    private static (int First, int Last) ParsePart(string part, int pageCount)
    {
        int dash = part.IndexOf('-');
        if (dash < 0)
        {
            int page = ParsePage(part, part, pageCount);
            return (page, page);
        }

        int first = ParsePage(part[..dash], part, pageCount);
        string lastText = part[(dash + 1)..].Trim();
        int last = lastText.Length == 0 ? pageCount : ParsePage(lastText, part, pageCount);
        if (last < first)
        {
            throw new FormatException($"'{part}' runs backwards.");
        }

        return (first, last);
    }

    private static int ParsePage(string text, string part, int pageCount)
    {
        if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, null, out int page))
        {
            throw new FormatException($"'{part}' is not a page or a range.");
        }

        if (page < 1 || page > pageCount)
        {
            throw new FormatException($"Page {page} is outside 1-{pageCount}.");
        }

        return page;
    }
}
