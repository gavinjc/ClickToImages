namespace ClickToImages.Core.Rendering;

// Named presets rather than a raw number, because "DPI" means nothing to most users (spec §4.2).
public enum ResolutionPreset
{
    Screen,
    Good,
    Print,
    Maximum,
}

public static class ResolutionPresets
{
    public const ResolutionPreset Default = ResolutionPreset.Good;

    public static int Dpi(this ResolutionPreset preset) => preset switch
    {
        ResolutionPreset.Screen => 96,
        ResolutionPreset.Good => PageRenderSettings.DefaultDpi,
        ResolutionPreset.Print => 300,
        ResolutionPreset.Maximum => 600,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };
}
