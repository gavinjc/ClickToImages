using System.Runtime.CompilerServices;

namespace ScaleRepro;

// Writes every .NET exception to ScaleRepro-crash.log beside the exe, from before App's constructor
// runs, so a variant that dies on start leaves its reason behind without the Event Viewer. A crash
// inside native XAML with no managed exception still leaves nothing; the exit code is all there is.
internal static class CrashLog
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "ScaleRepro-crash.log");

    [ModuleInitializer]
    internal static void Start()
    {
        File.WriteAllText(LogPath, $"{DateTime.Now:O} started {Environment.CommandLine}{Environment.NewLine}");
        // A first-chance exception carries only the throwing frame; the caller's stack says which call failed.
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            Append("first chance", e.Exception, Environment.StackTrace);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Append("UNHANDLED", e.ExceptionObject as Exception);
    }

    private static void Append(string kind, Exception? exception, string callerStack = "")
    {
        File.AppendAllText(LogPath, $"{DateTime.Now:O} {kind}: {exception}{Environment.NewLine}{callerStack}{Environment.NewLine}");
    }
}
