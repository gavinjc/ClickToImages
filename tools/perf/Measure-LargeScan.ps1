<#
Measures spec §5 on the installed package: launch the app the way the right-click verb does (a
.list file and ActivateApplication on the package's AUMID), then time the window, sample memory
and CPU, and optionally watch the page grid's tiles through UI Automation. Closes the app with
WM_CLOSE and waits; it never kills it. Run it through the hub's GUI wrapper:

    powershell -NoProfile -File run-gui-test.ps1 -Project ClickToImages
        -Command "powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\Measure-LargeScan.ps1 -PdfPath <scan.pdf>"

-Cold copies the PDF unbuffered (xcopy /j) to a fresh name before each run, so its pages aren't
in the file cache. The app's own binaries stay cached: only a reboot clears those.

-ObserveTiles polls the grid through UIA every 250 ms. That walks the app's UI thread, which also
runs the thumbnail loader, so keep it out of the runs that measure window time and memory.
Tiles are virtualized, so UIA only sees the realized ones; the 200-thumbnail time is taken from
CPU going idle instead.
#>
param(
    [Parameter(Mandatory)] [string] $PdfPath,
    [int] $Runs = 3,
    [int] $Seconds = 20,
    [switch] $Cold,
    [switch] $ObserveTiles,
    [string] $CsvPath
)

$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies UIAutomationClient, UIAutomationTypes, WindowsBase -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager
{
    void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
}

[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
class ApplicationActivationManager
{
}

public sealed class RunResult
{
    public uint ProcessId;
    public double WindowShownMs = -1;
    public double WindowUncloakedMs = -1;
    public IntPtr Window;
    public long PeakWorkingSet;
    public long PeakPrivateBytes;
    public long SampledPeakWorkingSet;
    public double SampledPeakAtMs;
    public double CpuIdleAtMs = -1;
    public double CpuSeconds;
    public double First30LoadedMs = -1;
    public double ScreenLoadedMs = -1;
    public double AllRealizedLoadedMs = -1;
    public int MaxRealizedTiles;
    public List<string> Samples = new List<string>();
}

public static class LaunchProbe
{
    private const string LoadingText = "Loading\u2026";
    private const int DwmCloaked = 14;
    private const uint WmClose = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    public static RunResult Run(string aumid, string arguments, int durationMs, bool observeTiles)
    {
        var result = new RunResult();
        var clock = Stopwatch.StartNew();
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        manager.ActivateApplication(aumid, arguments, 0, out result.ProcessId);

        Process process = Process.GetProcessById((int)result.ProcessId);
        Thread tiles = null;
        TimeSpan lastCpu = TimeSpan.Zero;
        double lastCpuMs = 0;
        double nextSampleMs = 0;

        while (clock.ElapsedMilliseconds < durationMs && !process.HasExited)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            if (result.WindowUncloakedMs < 0)
            {
                FindWindow(result, now);
                if (result.WindowUncloakedMs >= 0 && observeTiles)
                {
                    tiles = new Thread(() => WatchTiles(result, clock, durationMs));
                    tiles.IsBackground = true;
                    tiles.Start();
                }
            }

            if (now >= nextSampleMs)
            {
                process.Refresh();
                TimeSpan cpu = process.TotalProcessorTime;
                double percentOfOneCore = lastCpuMs > 0
                    ? (cpu - lastCpu).TotalMilliseconds / (now - lastCpuMs) * 100 : 0;
                lastCpu = cpu;
                lastCpuMs = now;
                if (process.WorkingSet64 > result.SampledPeakWorkingSet)
                {
                    result.SampledPeakWorkingSet = process.WorkingSet64;
                    result.SampledPeakAtMs = now;
                }

                result.Samples.Add(string.Format("{0:F0},{1},{2},{3:F1}",
                    now, process.WorkingSet64, process.PrivateMemorySize64, percentOfOneCore));
                nextSampleMs += 50;
            }

            Thread.Sleep(result.WindowUncloakedMs < 0 ? 2 : 10);
        }

        process.Refresh();
        result.PeakWorkingSet = process.PeakWorkingSet64;
        result.PeakPrivateBytes = process.PeakPagedMemorySize64;
        result.CpuSeconds = process.TotalProcessorTime.TotalSeconds;
        result.CpuIdleAtMs = FindCpuIdle(result.Samples);
        if (tiles != null)
        {
            tiles.Join();
        }

        return result;
    }

    // Graceful only: a force-kill of a WinUI app can break Windows App Runtime machine-wide.
    public static bool Close(RunResult result, int timeoutMs)
    {
        Process process = Process.GetProcessById((int)result.ProcessId);
        PostMessage(result.Window, WmClose, IntPtr.Zero, IntPtr.Zero);
        return process.WaitForExit(timeoutMs);
    }

    private static void FindWindow(RunResult result, double now)
    {
        EnumWindows((hwnd, lParam) =>
        {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner != result.ProcessId || !IsWindowVisible(hwnd))
            {
                return true;
            }

            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() != "WinUIDesktopWin32WindowClass")
            {
                return true;
            }

            result.Window = hwnd;
            if (result.WindowShownMs < 0)
            {
                result.WindowShownMs = now;
            }

            int cloaked;
            if (DwmGetWindowAttribute(hwnd, DwmCloaked, out cloaked, 4) == 0 && cloaked == 0)
            {
                result.WindowUncloakedMs = now;
            }

            return false;
        }, IntPtr.Zero);
    }

    // The last moment the process used more than a fifth of one core, averaged over 500 ms. The
    // thumbnails are the only work after load, so this is when the last of them finished.
    private static double FindCpuIdle(List<string> samples)
    {
        double idleAt = -1;
        var times = new List<double>();
        var cores = new List<double>();
        foreach (string sample in samples)
        {
            string[] fields = sample.Split(',');
            times.Add(double.Parse(fields[0]));
            cores.Add(double.Parse(fields[3]));
        }

        for (int i = 10; i < times.Count; i++)
        {
            double sum = 0;
            for (int j = i - 9; j <= i; j++)
            {
                sum += cores[j];
            }

            if (sum / 10 / 100 > 0.2)
            {
                idleAt = times[i];
            }
        }

        return idleAt;
    }

    private static void WatchTiles(RunResult result, Stopwatch clock, int durationMs)
    {
        var cache = new CacheRequest();
        cache.TreeScope = TreeScope.Subtree;
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.AutomationIdProperty);

        AutomationElement window = AutomationElement.FromHandle(result.Window);
        var gridCondition = new PropertyCondition(AutomationElement.AutomationIdProperty, "PagesGridView");
        while (clock.ElapsedMilliseconds < durationMs && result.AllRealizedLoadedMs < 0)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            try
            {
                AutomationElement grid;
                using (cache.Activate())
                {
                    grid = window.FindFirst(TreeScope.Descendants, gridCondition);
                }

                if (grid != null)
                {
                    ReadTiles(result, grid, now);
                }
            }
            catch (ElementNotAvailableException)
            {
                // The tree changed under the walk; the next poll sees the new one.
            }

            Thread.Sleep(250);
        }
    }

    private static void ReadTiles(RunResult result, AutomationElement grid, double now)
    {
        var tiles = new List<AutomationElement>();
        CollectTiles(grid, tiles);
        if (tiles.Count == 0)
        {
            return;
        }

        result.MaxRealizedTiles = Math.Max(result.MaxRealizedTiles, tiles.Count);
        System.Windows.Rect viewport = grid.Cached.BoundingRectangle;
        int loadedInOrder = 0;
        bool orderBroken = false;
        bool screenLoaded = true;
        bool allLoaded = true;
        foreach (AutomationElement tile in tiles)
        {
            bool loading = ContainsLoading(tile);
            allLoaded &= !loading;
            if (!loading && !orderBroken)
            {
                loadedInOrder++;
            }

            orderBroken |= loading;
            if (loading && tile.Cached.BoundingRectangle.IntersectsWith(viewport))
            {
                screenLoaded = false;
            }
        }

        if (result.First30LoadedMs < 0 && loadedInOrder >= 30)
        {
            result.First30LoadedMs = now;
        }

        if (result.ScreenLoadedMs < 0 && screenLoaded)
        {
            result.ScreenLoadedMs = now;
        }

        if (allLoaded && tiles.Count >= 30)
        {
            result.AllRealizedLoadedMs = now;
        }
    }

    private static void CollectTiles(AutomationElement element, List<AutomationElement> tiles)
    {
        foreach (AutomationElement child in element.CachedChildren)
        {
            if (child.Cached.ControlType == ControlType.ListItem)
            {
                tiles.Add(child);
            }
            else
            {
                CollectTiles(child, tiles);
            }
        }
    }

    private static bool ContainsLoading(AutomationElement element)
    {
        foreach (AutomationElement child in element.CachedChildren)
        {
            if (child.Cached.Name == LoadingText || ContainsLoading(child))
            {
                return true;
            }
        }

        return false;
    }
}
'@

$package = Get-AppxPackage TheBureauofStuff.ClickToImages
if (-not $package)
{
    throw 'TheBureauofStuff.ClickToImages is not installed.'
}

$aumid = "$($package.PackageFamilyName)!App"
Write-Host "Package $($package.Name) $($package.Version), AUMID $aumid"

$PdfPath = [IO.Path]::GetFullPath($PdfPath)
if (-not (Test-Path $PdfPath))
{
    & (Join-Path $PSScriptRoot 'New-ScanPdf.ps1') -Path $PdfPath
}

$launchDir = Join-Path $env:LOCALAPPDATA 'Temp\ClickToImages'
New-Item -ItemType Directory -Force $launchDir | Out-Null

function Format-Ms([double] $ms)
{
    if ($ms -lt 0) { return 'n/a' }
    return '{0:N0} ms' -f $ms
}

$rows = @()
for ($run = 1; $run -le $Runs; $run++)
{
    if (Get-Process ClickToImages.App -ErrorAction SilentlyContinue)
    {
        throw 'ClickToImages.App is already running; close it first (this script never kills it).'
    }

    $target = $PdfPath
    if ($Cold)
    {
        $coldDir = Join-Path ([IO.Path]::GetDirectoryName($PdfPath)) "cold-$run-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
        New-Item -ItemType Directory $coldDir | Out-Null
        xcopy /j /q $PdfPath "$coldDir\" | Out-Null
        $target = Join-Path $coldDir ([IO.Path]::GetFileName($PdfPath))
    }

    $listFile = Join-Path $launchDir "$([Guid]::NewGuid()).list"
    [IO.File]::WriteAllText($listFile, "$target`n", (New-Object Text.UTF8Encoding $false))

    $result = [LaunchProbe]::Run($aumid, "--list `"$listFile`"", $Seconds * 1000, $ObserveTiles.IsPresent)
    $closed = $result.Window -ne [IntPtr]::Zero -and [LaunchProbe]::Close($result, 20000)

    $row = [pscustomobject]@{
        Run              = $run
        Cold             = $Cold.IsPresent
        WindowShown      = Format-Ms $result.WindowShownMs
        WindowUncloaked  = Format-Ms $result.WindowUncloakedMs
        PeakWorkingSetMB = [math]::Round($result.PeakWorkingSet / 1MB)
        PeakPrivateMB    = [math]::Round($result.PeakPrivateBytes / 1MB)
        SampledPeakAt    = Format-Ms $result.SampledPeakAtMs
        CpuIdleAt        = Format-Ms $result.CpuIdleAtMs
        CpuSeconds       = [math]::Round($result.CpuSeconds, 1)
        First30Loaded    = Format-Ms $result.First30LoadedMs
        ScreenLoaded     = Format-Ms $result.ScreenLoadedMs
        RealizedLoaded   = Format-Ms $result.AllRealizedLoadedMs
        RealizedTiles    = $result.MaxRealizedTiles
        ClosedCleanly    = $closed
    }
    $rows += $row
    $row | Format-List | Out-String | Write-Host

    if ($CsvPath)
    {
        $samplesFile = [IO.Path]::ChangeExtension($CsvPath, ".run$run.samples.csv")
        Set-Content $samplesFile (@('ms,workingSet,privateBytes,cpuPercentOfOneCore') + $result.Samples)
    }

    if (-not $closed)
    {
        throw "Run $run did not close within 20 s (pid $($result.ProcessId)); left running, close it by hand."
    }

    if ($Cold)
    {
        Remove-Item -Recurse $coldDir
    }

    Start-Sleep -Seconds 3
}

if ($CsvPath)
{
    $rows | Export-Csv -NoTypeInformation $CsvPath
}
