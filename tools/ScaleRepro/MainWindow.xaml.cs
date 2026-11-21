using System.Reflection;
using System.Runtime.InteropServices;
#if REPRO_BUREAU
using Bureau.Common.Messages;
#endif
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ScaleRepro;

public sealed partial class MainWindow : Window
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private double _lastScale;

    public MainWindow(HashSet<string> switches)
    {
        InitializeComponent();

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string variant = typeof(MainWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ReproVariant")?.Value ?? "baseline";
        string on = switches.Count == 0 ? "" : " + " + string.Join(" + ", App.KnownSwitches.Where(switches.Contains));
        Title = $"ScaleRepro {configuration}: {variant}{on}";
        HeaderText.Text = Title;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
        if (switches.Contains("icon"))
        {
            _appWindow.SetIcon("Assets\\ClickToImages.ico");
        }

        if (switches.Contains("mica"))
        {
            SystemBackdrop = new MicaBackdrop();
        }
        else if (switches.Contains("desktopacrylic"))
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }

        if (switches.Contains("customtitle"))
        {
            CustomTitleText.Text = Title;
            CustomTitleBar.Visibility = Visibility.Visible;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);
        }

#if REPRO_BUREAU
        // The apps paint their root with this opaque brush, which would cover a backdrop.
        if (switches.Contains("palette"))
        {
            RootGrid.Background = (Brush)Application.Current.Resources["AppCanvasBackgroundBrush"];
        }

        if (switches.Contains("darktitle"))
        {
            Bureau.Common.TitleBarTheme.Follow(this, RootGrid);
        }

        // A visible row, as the apps show for skipped files: the ThemeShadow and Translation Z
        // only exist once the bar is shown.
        MessageBar? messages = null;
        if (switches.Contains("messagebar"))
        {
            messages = new MessageBar { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8) };
            ChromeHost.Children.Add(messages);
            messages.Show("skipped", MessageSeverity.Warning, "Skipped notes.txt");
        }

        if (switches.Contains("addbutton"))
        {
            ChromeHost.Children.Add(new AddButton
            {
                KeepAbove = messages,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(24),
            });
        }
#endif

        // Shows that the window really did see the new scale, so a garbled window at the right
        // scale can be told apart from one that never got the change.
        RootGrid.Loaded += (_, _) =>
        {
            _lastScale = RootGrid.XamlRoot.RasterizationScale;
            ShowScale();
            RootGrid.XamlRoot.Changed += (root, _) =>
            {
                ShowScale();
                if (root.RasterizationScale == _lastScale)
                {
                    return;
                }

                _lastScale = root.RasterizationScale;
                if (switches.Contains("relayout"))
                {
                    RebuildContent();
                }

                if (switches.Contains("resizenudge"))
                {
                    NudgeSize();
                }
            };
        };
    }

    // Collapsing the root throws away every element's rendered content; showing it again on the
    // next tick makes XAML draw it all afresh at the current scale. A plain InvalidateMeasure
    // wouldn't: layout sizes in DIPs don't change with the scale, so nothing would redraw.
    private void RebuildContent()
    {
        RootGrid.Visibility = Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            RootGrid.Visibility = Visibility.Visible;
            RootGrid.InvalidateMeasure();
            RootGrid.UpdateLayout();
        });
    }

    // One pixel wider and back: a real window resize, which makes the swap chain and DWM's surface
    // resize and present a new frame, for the case where the stale frame is below XAML.
    private void NudgeSize()
    {
        SizeInt32 size = _appWindow.Size;
        _appWindow.Resize(new SizeInt32(size.Width + 1, size.Height));
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _appWindow.Resize(size));
    }

    // The runtime part tells which Windows App Runtime actually loaded, which for a
    // framework-dependent build is the installed framework package, not the NuGet version. The 1.5
    // projection doesn't expose RuntimeInfo, so that variant shows only the NuGet version.
    private void ShowScale()
    {
        string sdk = typeof(MainWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ReproSdkVersion")?.Value ?? "?";
#if REPRO_SDK2
        // Self-contained 2.5.4-experimental throws here: RuntimeInfo wants
        // Microsoft.WindowsAppRuntime.Insights.Resource.dll, which no 2.5 package ships. Thrown from
        // a Loaded handler it ended the process with 0xC000027B.
        string runtime;
        try
        {
            runtime = $"   runtime {Microsoft.Windows.ApplicationModel.WindowsAppRuntime.RuntimeInfo.AsString}";
        }
        catch (System.Runtime.InteropServices.COMException e)
        {
            runtime = $"   runtime unknown (0x{e.HResult:X8})";
        }
#else
        string runtime = "";
#endif
        ScaleText.Text =
            $"RasterizationScale {RootGrid.XamlRoot.RasterizationScale:0.##}" +
            $"   window DPI {GetDpiForWindow(_hwnd)}   awareness {DpiAwarenessName()}\n" +
            $"SDK {sdk}{runtime}";
    }

    private string DpiAwarenessName()
    {
        IntPtr context = GetWindowDpiAwarenessContext(_hwnd);
        if (AreDpiAwarenessContextsEqual(context, PerMonitorAwareV2))
        {
            return "PerMonitorV2";
        }

        return GetAwarenessFromDpiAwarenessContext(context) switch
        {
            0 => "Unaware",
            1 => "System",
            2 => "PerMonitor (v1)",
            int other => $"unknown ({other})",
        };
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
}
