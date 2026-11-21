using ClickToImages.App.Diagnostics;
using ClickToImages.App.State;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ClickToImages.App;

public partial class App : Application
{
    private static readonly ResourceLoader ResourceLoader = new();

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        AppLog.PruneOldLogs();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    // Fires on the UI thread for XAML/binding/most app-code exceptions. Handled = true keeps the
    // process alive rather than terminating immediately — deliberate: losing a page selection the
    // user has spent time on to a silent crash from one bad code path is worse than surfacing a
    // message and letting them keep going. AppDomain's handler below covers the case where that
    // trade-off isn't available at all.
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.LogError("UnhandledException", e.Exception);
        e.Handled = true;

        // Before the window exists there is nothing to keep going in: staying alive would leave a
        // headless process behind every failed right-click, with no way for the user to close it.
        if (_window is null)
        {
            Exit();
            return;
        }

        try
        {
            _window?.ShowErrorMessage(ResourceLoader.GetString("UnexpectedErrorMessage"));
        }
        catch (Exception)
        {
            // Best-effort — the log write above already captured what matters.
        }
    }

    // Background-thread exceptions land here instead — by the time this fires the process is
    // already terminating (IsTerminating is true in practice on .NET), so this is purely
    // "log before you die", not a place to attempt any UI recovery.
    private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLog.LogError("AppDomainUnhandledException", ex);
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Unpackaged app — command-line args come through Environment, not LaunchActivatedEventArgs.
        // Element 0 is the exe path.
        LaunchArguments launch = LaunchArguments.Parse(Environment.GetCommandLineArgs()[1..]);

        IReadOnlyList<string> pdfPaths = launch.PdfPaths;
        bool listUnreadable = false;
        if (launch.ListPath is not null)
        {
            string[]? listedPaths = TryReadTempFile(launch.ListPath);
            listUnreadable = listedPaths is null;
            pdfPaths = listedPaths ?? [];
        }

        // An unreadable .skipped file only costs the warning about which items were left out; the
        // PDFs still open, so it gets no message of its own beyond the log.
        IReadOnlyList<string> skippedNames =
            (launch.SkippedPath is not null ? TryReadTempFile(launch.SkippedPath) : null) ?? [];

        _window = new MainWindow(pdfPaths, skippedNames);
        if (listUnreadable)
        {
            _window.ShowErrorMessage(ResourceLoader.GetString("LaunchListUnreadableMessage"));
        }

        _window.Activate();
    }

    // Null when the shell extension's temp file has gone (a temp sweep) or can't be opened. The
    // window still opens, so a right-click never ends in nothing visible.
    private static string[]? TryReadTempFile(string path)
    {
        try
        {
            return LaunchArguments.ReadAndDeleteTempFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogError("ReadLaunchTempFile", ex);
            return null;
        }
    }
}
