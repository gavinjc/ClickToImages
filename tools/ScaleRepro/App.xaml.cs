using Microsoft.UI.Xaml;

namespace ScaleRepro;

public partial class App : Application
{
    // Round 1: each adds one thing the ClickTo apps do that WinUI 3 Gallery doesn't; "all" turns
    // these on. The Bureau.Common ones only exist on the baseline's SDK version (see ScaleRepro.csproj).
#if REPRO_BUREAU
    private static readonly string[] ChromeSwitches = ["palette", "darktitle", "icon", "messagebar", "addbutton"];
#else
    private static readonly string[] ChromeSwitches = ["icon"];
#endif

    // Round 4: what Gallery does that the apps don't (a backdrop, its own title bar), and two
    // workarounds that force a redraw after a scale change. Not part of "all".
    private static readonly string[] WindowSwitches = ["mica", "desktopacrylic", "customtitle", "relayout", "resizenudge"];

    public static readonly string[] KnownSwitches = [.. ChromeSwitches, .. WindowSwitches];

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        HashSet<string> switches = ParseSwitches(Environment.GetCommandLineArgs()[1..]);

        // Appended after XamlControlsResources, the same order as the apps' App.xaml. KnownSwitches
        // has no "palette" without Bureau.Common, so this never asks for a dictionary that isn't there.
        if (switches.Contains("palette"))
        {
            Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("ms-appx:///Bureau.Common/Themes/Palette.xaml") });
            Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("ms-appx:///Bureau.Common/Themes/Styles.xaml") });
        }

        _window = new MainWindow(switches);
        _window.Activate();
    }

    private static HashSet<string> ParseSwitches(string[] args)
    {
        HashSet<string> switches = new(StringComparer.OrdinalIgnoreCase);
        // "mica-customtitle" is read as mica plus customtitle, so a combination can be named in one word.
        foreach (string name in args.SelectMany(arg => arg.TrimStart('-', '/').ToLowerInvariant().Split('-')))
        {
            if (name == "all")
            {
                switches.UnionWith(ChromeSwitches);
            }
            else if (KnownSwitches.Contains(name))
            {
                switches.Add(name);
            }
        }

        return switches;
    }
}
