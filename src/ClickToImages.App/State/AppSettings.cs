using System.Text.Json;
using ClickToImages.Core;
using ClickToImages.Core.Export;

namespace ClickToImages.App.State;

// Ported from ClickTo: Slideshow. The export options are the only persisted state.
public static class AppSettings
{
    private static readonly string DefaultFilePath = Path.Combine(AppDataFolders.LocalRoot, "settings.json");

    /// <summary>
    /// Best-effort: a missing or corrupt settings file gives the defaults rather than throwing, and
    /// any single value that can't be used any more (an enum from a newer version, a chosen folder
    /// since deleted or on a drive now unplugged) falls back to its own default alone.
    /// </summary>
    public static ExportOptions Load(string? filePath = null)
    {
        ExportOptions? loaded;
        try
        {
            string path = filePath ?? DefaultFilePath;
            if (!File.Exists(path))
            {
                return new ExportOptions();
            }

            loaded = JsonSerializer.Deserialize<ExportOptions>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ExportOptions();
        }

        return loaded is null ? new ExportOptions() : WithUnusableValuesReset(loaded);
    }

    /// <summary>Best-effort: a failed write is swallowed rather than interrupting shutdown.</summary>
    public static void Save(ExportOptions options, string? filePath = null)
    {
        try
        {
            string path = filePath ?? DefaultFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A settings file that didn't get written only costs the user their options next time.
        }
    }

    private static ExportOptions WithUnusableValuesReset(ExportOptions loaded)
    {
        var defaults = new ExportOptions();
        bool chosenFolderUsable = loaded.ChosenFolder is not null && Directory.Exists(loaded.ChosenFolder);
        return loaded with
        {
            Resolution = Enum.IsDefined(loaded.Resolution) ? loaded.Resolution : defaults.Resolution,
            Format = Enum.IsDefined(loaded.Format) ? loaded.Format : defaults.Format,
            JpegQuality = loaded.JpegQuality is >= 1 and <= 100 ? loaded.JpegQuality : defaults.JpegQuality,
            OutputLocation = !Enum.IsDefined(loaded.OutputLocation) ||
                (loaded.OutputLocation == OutputLocation.ChosenFolder && !chosenFolderUsable)
                ? defaults.OutputLocation
                : loaded.OutputLocation,
            ChosenFolder = chosenFolderUsable ? loaded.ChosenFolder : null,
            NamingPattern = loaded.NamingPattern is not null && FileNamePattern.FindProblem(loaded.NamingPattern) is null
                ? loaded.NamingPattern
                : defaults.NamingPattern,
        };
    }
}
