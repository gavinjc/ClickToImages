using System.Runtime.InteropServices;

namespace ClickToImages.Core.Rendering;

public enum PdfOpenProblem
{
    // No password, or the wrong one, for a PDF that needs one.
    PasswordRefused,

    // AES-256 (/V 5 /R 6): Windows.Data.Pdf can't open it with any password, not even a
    // permissions-only PDF whose user password is empty (spec §4.2).
    UnsupportedEncryption,
}

public static class PdfOpenFailures
{
    // HRESULT_FROM_WIN32(ERROR_WRONG_PASSWORD). Measured against RC4-128 (R3) PDFs: the same for no
    // password, an empty one and a wrong one.
    private const int WrongPassword = unchecked((int)0x8007052B);

    private const int Aes256Encryption = unchecked((int)0x80048040);

    /// <summary>
    /// What a <see cref="PdfPageRenderer.OpenAsync"/> failure means, when it is one of the
    /// encryption cases; null for anything else (a corrupt or unreadable file).
    /// </summary>
    public static PdfOpenProblem? Classify(Exception exception) => exception switch
    {
        COMException { HResult: WrongPassword } => PdfOpenProblem.PasswordRefused,
        COMException { HResult: Aes256Encryption } => PdfOpenProblem.UnsupportedEncryption,
        _ => null,
    };

    /// <summary>
    /// The system's reason for an open failure, fit to show under "Couldn't open this PDF.": the
    /// first paragraph of a file-system error ("Access is denied."), or null when the PDF itself
    /// is bad. Windows.Data.Pdf's own HRESULTs (0x8004xxxx) have no system message text, so their
    /// message is empty or reads "The text associated with this error code could not be found.
    /// Invalid file, zero length. 0x8004808e".
    /// </summary>
    public static string? ReadableReason(Exception exception)
    {
        const int FacilityWin32 = 7;
        if ((exception.HResult >> 16 & 0x1FFF) != FacilityWin32)
        {
            return null;
        }

        string firstParagraph = exception.Message.Split('\r', '\n')[0].Trim();
        return firstParagraph.Length == 0 ? null : firstParagraph;
    }
}
