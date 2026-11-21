namespace ClickToImages.App.ViewModels;

public enum SkipReason
{
    // The prompt was cancelled, or there was no one to ask.
    NoPasswordGiven,
    WrongPassword,

    // AES-256, which Windows.Data.Pdf can't open with any password (spec §4.2).
    UnsupportedEncryption,

    // Already in the list (same full path, any case); not opened twice.
    AlreadyAdded,
}

public sealed record SkippedDocument(string FileName, SkipReason Reason);
