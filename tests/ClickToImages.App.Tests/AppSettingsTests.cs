using ClickToImages.App.State;
using ClickToImages.Core.Export;
using ClickToImages.Core.Rendering;

namespace ClickToImages.App.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ClickToImagesSettingsTests").FullName;
    private readonly string _path;

    public AppSettingsTests()
    {
        _path = Path.Combine(_folder, "settings.json");
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryOption()
    {
        var saved = new ExportOptions
        {
            Resolution = ResolutionPreset.Maximum,
            Format = PageImageFormat.Tiff,
            JpegQuality = 40,
            TransparentBackground = true,
            OutputLocation = OutputLocation.ChosenFolder,
            ChosenFolder = _folder,
            NamingPattern = "{page} - {name}",
        };

        AppSettings.Save(saved, _path);

        Assert.Equal(saved, AppSettings.Load(_path));
    }

    [Fact]
    public void Load_MissingOrCorruptFile_GivesDefaults()
    {
        Assert.Equal(new ExportOptions(), AppSettings.Load(_path));

        File.WriteAllText(_path, "{ not json");

        Assert.Equal(new ExportOptions(), AppSettings.Load(_path));
    }

    // As a newer version, or a hand edit, might leave it: each bad value resets alone.
    [Fact]
    public void Load_ResetsOnlyTheValuesThatCantBeUsed()
    {
        string gone = Path.Combine(_folder, "deleted since");
        File.WriteAllText(_path, $$"""
            { "Resolution": 9, "Format": 1, "JpegQuality": 0, "OutputLocation": 2,
              "ChosenFolder": {{System.Text.Json.JsonSerializer.Serialize(gone)}}, "NamingPattern": "a/b" }
            """);

        ExportOptions loaded = AppSettings.Load(_path);

        Assert.Equal(
            (ResolutionPreset.Good, PageImageFormat.Jpeg, 90, OutputLocation.SubfolderNamedAfterPdf, (string?)null,
                FileNamePattern.Default),
            (loaded.Resolution, loaded.Format, loaded.JpegQuality, loaded.OutputLocation, loaded.ChosenFolder,
                loaded.NamingPattern));
    }
}
