using System.Text.Json.Serialization;
using ClickToImages.Core.Export;
using ClickToImages.Core.Rendering;

namespace ClickToImages.App.State;

/// <summary>
/// What the options panel sets (spec §4.3), and the app's only persisted state: AppSettings
/// writes this record as it is. Every property has a usable default, so a settings file from an
/// older version, missing some of them, still loads.
/// </summary>
public sealed record ExportOptions
{
    public ResolutionPreset Resolution { get; init; } = ResolutionPresets.Default;

    public PageImageFormat Format { get; init; } = PageImageFormat.Png;

    public int JpegQuality { get; init; } = PageRenderSettings.DefaultJpegQuality;

    // The user's choice, kept while a format without transparency is picked so that switching
    // back to PNG restores it; RenderSettings applies it to PNG only.
    public bool TransparentBackground { get; init; }

    public OutputLocation OutputLocation { get; init; } = OutputLocation.SubfolderNamedAfterPdf;

    // Only used with OutputLocation.ChosenFolder, but kept when another location is picked, so the
    // folder is still there if the user switches back.
    public string? ChosenFolder { get; init; }

    public string NamingPattern { get; init; } = FileNamePattern.Default;

    [JsonIgnore]
    public PageRenderSettings RenderSettings => new()
    {
        Dpi = Resolution.Dpi(),
        Format = Format,
        JpegQuality = JpegQuality,
        TransparentBackground = TransparentBackground && Format.SupportsTransparency(),
    };

    [JsonIgnore]
    public FileNamePatternProblem? NamingProblem => FileNamePattern.FindProblem(NamingPattern);

    public string OutputFolder(string pdfPath) => OutputLocation.FolderFor(pdfPath, ChosenFolder);

    /// <summary>
    /// <paramref name="documentName"/> fills <c>{name}</c> (see <see cref="FileNamePattern.FileName"/>).
    /// Only valid while <see cref="NamingProblem"/> is null.
    /// </summary>
    public string FileName(string documentName, int pageNumber, int pageCount) =>
        new FileNamePattern(NamingPattern).FileName(documentName, pageNumber, pageCount, Resolution.Dpi(), Format);
}
