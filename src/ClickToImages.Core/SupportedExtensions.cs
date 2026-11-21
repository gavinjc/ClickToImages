namespace ClickToImages.Core;

// The files the app and CLI will accept; the CLI checks its input against it, the app does too.
// ShellExtension/SupportedExtensions.h holds the same list for the context menu's visibility gate;
// SupportedExtensionsSyncTests fails `dotnet test` (not the build) if they drift.
public static class SupportedExtensions
{
    public static readonly IReadOnlyList<string> All =
    [
        ".pdf",
    ];

    public static bool Includes(string path) =>
        All.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
