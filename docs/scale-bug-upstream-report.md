# Upstream report: window shows a stale frame and redraws at the wrong scale after moving to a monitor with a different scale

Posted 2026-10-29 as https://github.com/microsoft/microsoft-ui-xaml/issues/12464.

---

## Title

Unpackaged WinUI 3 window keeps a stale frame after moving to a monitor with a different scale;
any interaction then redraws controls at the wrong size over it (2.3.1 and 2.6.4; fine on
2.5.4-experimental)

## Description

A bare WinUI 3 window (a few Buttons, a TextBox, a CheckBox, a ComboBox and some TextBlocks),
moved from the primary monitor to a second monitor that uses a different display scale, stops
presenting new frames correctly. Moved with no interaction it looks right. As soon as any control
is used on the second monitor, text and icons in buttons, text boxes and headers are drawn at the
wrong size, overlapping, and in places as the wrong glyphs (letters replaced by CheckBox and
circle icons from Segoe Fluent Icons; "7..12" in a TextBox drew as "ct..12"). Bitmaps and plain
filled rectangles look right.

The window does receive the new scale: `XamlRoot.RasterizationScale` and `GetDpiForWindow` both
change to the second monitor's values when it crosses.

Moving back to the first monitor restores the window. Moving to the second monitor again
garbles it again. With both monitors set to the same scale it never happens.

WinUI 3 Gallery 3.0.4.0 (Windows App SDK 2.5 experimental, Windows App Runtime 2.5.4.0) does
not show it on the same monitors, and neither does the repro below built on 2.5.4-experimental.
It reproduces on stable 2.3.1 and 2.6.4.

## Steps to reproduce

1. Two monitors with different scales: monitor 1 at 150%
   2560x1600, monitor 2 at 100% 1920x1080.
2. Build and run the project below (unpackaged and framework-dependent; the self-contained build
   behaves the same).
3. With the window on monitor 1, change the ComboBox from PNG to TIFF.
4. Drag the window to monitor 2 without clicking anything.
5. Click any control on monitor 2 (open the ComboBox, type in the TextBox).

**Expected:** on monitor 2 the window shows TIFF in the ComboBox, and every control redraws at
monitor 2's scale.

**Actual:** after step 4 the window on monitor 2 still shows **PNG**, a frame from before the
change in step 3, so it is presenting a stale frame and not re-rendering. After step 5 parts of
the window redraw at the wrong scale on top of that stale frame: wrong-size and overlapping text,
and wrong glyphs. Moving the window back to monitor 1 shows TIFF and draws correctly.

## Environment

- Windows 11 Pro **10.0.26300** (Insider build).
- Screen 1 (main): 2560x1600 @ 150% scale — Intel Graphics (Intel Core Ultra 7 255HX Processor), driver 32.0.101.9150
- Screen 2: 1920x1080 @ 100% scale — NVIDIA GeForce RTX 3070 Mobile 8GB, driver 32.0.16.1714
- The two screens are driven by different GPUs (hybrid laptop: the Intel iGPU drives screen 1 and
  the NVIDIA dGPU drives screen 2).
- Windows App SDK **2.3.1** (Microsoft.WindowsAppSDK and Microsoft.WindowsAppSDK.Runtime 2.3.1;
  WinUI component 2.3.0, InteractiveExperiences 2.1.3). Framework-dependent and self-contained.
- Also reproduces on **2.6.4** self-contained (all components 2.6.4).
- Does **not** reproduce on **2.5.4-experimental** self-contained, the runtime WinUI 3 Gallery
  3.0.4.0 uses. Same project and steps; only the SDK version differs. The components that differ:

  | Component | 2.3.1 (garbles) | 2.6.4 (garbles) | 2.5.4-experimental (clean) |
  |---|---|---|---|
  | Microsoft.WindowsAppSDK.WinUI | 2.3.0 | 2.6.4 | 2.3.10-experimental |
  | Microsoft.WindowsAppSDK.InteractiveExperiences | 2.1.3 | 2.6.4 | 2.1.10-experimental |
  | Microsoft.WindowsAppSDK.Foundation | 2.3.5 | 2.6.4 | 2.3.13-experimental |
  | Microsoft.WindowsAppSDK.DWrite | 2.1.0 | 2.6.4 | 2.1.1-experimental |

  So the fix seems to be in the experimental WinUI or InteractiveExperiences builds and has not
  reached the stable 2.6.4, despite its higher version numbers.
- .NET 8, `net8.0-windows10.0.19041.0`, `TargetPlatformMinVersion` 10.0.17763.0, x64, unpackaged
  (`WindowsPackageType=None`). Also reproduces in MSIX-packaged apps (a wapproj packaging the same
  exe) installed from the Store and side-loaded.
- `app.manifest` with `PerMonitorV2` (also tried the template's `PerMonitorV2, PerMonitor`, and no
  manifest at all).

## What doesn't change it

Each of these was tried one at a time on the bare window; the window still garbles:

- Windows App SDK 2.6.4 (self-contained) in place of 2.3.1.
- Self-contained in place of framework-dependent.
- The stock template `app.manifest` (`PerMonitorV2, PerMonitor`) and no `app.manifest` at all.
- Debug and Release builds.
- `SystemBackdrop = new MicaBackdrop()`, and `new DesktopAcrylicBackdrop()`.
- `ExtendsContentIntoTitleBar = true` with `SetTitleBar` on a custom title-bar element, alone and
  together with Mica (the closest match to WinUI 3 Gallery's window).
- Forcing a redraw on `XamlRoot.Changed` when `RasterizationScale` changes: collapsing the root
  element and making it visible again on the next dispatcher tick (with `InvalidateMeasure` and
  `UpdateLayout`); and resizing the window one pixel wider and back with `AppWindow.Resize`.
- Our apps' own additions, none of which are needed to trigger it: merged theme dictionaries,
  the DWM immersive dark title bar (`DwmSetWindowAttribute` 20), `AppWindow.SetIcon`, an element
  with `Translation` Z and a `ThemeShadow`, and a button animated with `TranslationTransition`.

## Repro project

`App.xaml` holds only `XamlControlsResources`; `App.xaml.cs` creates and activates `MainWindow`.

**ScaleRepro.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RootNamespace>ScaleRepro</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.3.1" />
    <PackageReference Include="Microsoft.WindowsAppSDK.Runtime" Version="2.3.1" />
  </ItemGroup>
</Project>
```

**app.manifest** (the relevant part)

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
  </windowsSettings>
</application>
```

**MainWindow.xaml**

```xml
<Window x:Class="ScaleRepro.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Padding="24" Spacing="12" HorizontalAlignment="Left">
        <TextBlock x:Name="ScaleText" Style="{StaticResource CaptionTextBlockStyle}"/>
        <TextBlock Text="The quick brown fox jumps over the lazy dog 0123456789"/>
        <StackPanel Orientation="Horizontal" Spacing="8">
            <Button Content="Export"/>
            <Button>
                <StackPanel Orientation="Horizontal" Spacing="6">
                    <FontIcon Glyph="&#xE7AD;" FontSize="14"/>
                    <TextBlock Text="Rotate"/>
                </StackPanel>
            </Button>
            <Button Style="{StaticResource AccentButtonStyle}" Content="Save"/>
        </StackPanel>
        <TextBox Header="Pages" Text="1-3, 5, 7..12" Width="240"/>
        <CheckBox Content="Transparent background" IsChecked="True"/>
        <ComboBox Header="Format" SelectedIndex="0" Width="240">
            <x:String>PNG</x:String>
            <x:String>JPEG</x:String>
            <x:String>TIFF</x:String>
        </ComboBox>
        <Rectangle Height="40" Width="240" Fill="{ThemeResource AccentFillColorDefaultBrush}"/>
    </StackPanel>
</Window>
```

**MainWindow.xaml.cs**: shows the scale so the move can be seen to arrive.

```csharp
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ScaleText.Loaded += (_, _) =>
        {
            ShowScale();
            ScaleText.XamlRoot.Changed += (_, _) => ShowScale();
        };
    }

    private void ShowScale() =>
        ScaleText.Text = $"RasterizationScale {ScaleText.XamlRoot.RasterizationScale:0.##}";
}
```

## Side note found while testing

On 2.5.4-experimental self-contained, `RuntimeInfo.AsString` throws `COMException 0x8007007E`
("Unable to load resource dll. Microsoft.WindowsAppRuntime.Insights.Resource.dll"). No 2.5 NuGet
package contains that DLL. Thrown from a `Loaded` handler, it ends the process with 0xC000027B.
2.6.4 self-contained returns normally.
