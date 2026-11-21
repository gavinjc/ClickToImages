using System.Globalization;
using System.Text;
using ClickToImages.Core.Rendering;

namespace ClickToImages.Core.Export;

public enum FileNamePatternProblem
{
    Empty,
    UnknownToken,
    UnclosedBrace,
    InvalidCharacter,
}

/// <summary>
/// The user-editable name each exported page gets, before its extension: literal text plus the
/// tokens <c>{name}</c> (the PDF's file name), <c>{page}</c>, <c>{total}</c> (the document's page
/// count) and <c>{dpi}</c> (spec §4.3). Tokens are case-insensitive.
/// </summary>
public sealed class FileNamePattern
{
    // report-p007.png for page 7 of 200.
    public const string Default = "{name}-p{page}";

    private static readonly string[] Tokens = ["name", "page", "total", "dpi"];

    private readonly string text;

    public FileNamePattern(string text)
    {
        if (FindProblem(text) is { } problem)
        {
            throw new FormatException($"'{text}' is not a usable file name pattern: {problem}.");
        }

        this.text = text;
    }

    /// <summary>
    /// Null when <paramref name="text"/> is usable. The tokens' own values are always valid in a
    /// file name, so only the literal text needs checking for characters Windows refuses.
    /// </summary>
    public static FileNamePatternProblem? FindProblem(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return FileNamePatternProblem.Empty;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                int close = text.IndexOf('}', i + 1);
                if (close < 0)
                {
                    return FileNamePatternProblem.UnclosedBrace;
                }

                if (!Tokens.Contains(text[(i + 1)..close], StringComparer.OrdinalIgnoreCase))
                {
                    return FileNamePatternProblem.UnknownToken;
                }

                i = close;
            }
            else if (Path.GetInvalidFileNameChars().Contains(text[i]))
            {
                return FileNamePatternProblem.InvalidCharacter;
            }
        }

        return null;
    }

    // A usable pattern's braces always pair into known tokens, so a plain search is exact.
    public bool NumbersPages => text.Contains("{page}", StringComparison.OrdinalIgnoreCase);

    public bool NamesDocument => text.Contains("{name}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="documentName"/> fills <c>{name}</c>: the PDF's file name without its
    /// extension, or the name <see cref="DocumentNames.Distinguish"/> gave it. <c>{page}</c> is
    /// zero-padded to the width of the document's page count, so the files sort in page order
    /// everywhere, not only in Explorer's natural sort.
    /// </summary>
    public string FileName(string documentName, int pageNumber, int pageCount, int dpi, PageImageFormat format)
    {
        string total = pageCount.ToString(CultureInfo.InvariantCulture);
        var name = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                name.Append(text[i]);
                continue;
            }

            int close = text.IndexOf('}', i + 1);
            name.Append(text[(i + 1)..close].ToLowerInvariant() switch
            {
                "name" => documentName,
                "page" => pageNumber.ToString(CultureInfo.InvariantCulture).PadLeft(total.Length, '0'),
                "total" => total,
                _ => dpi.ToString(CultureInfo.InvariantCulture),
            });
            i = close;
        }

        return name.Append(format.FileExtension()).ToString();
    }
}
