using System.Text;

namespace ClickToImages.App.State;

// The command line the app was started with, minus the exe path. The shell extension's contract
// is `--list <file> [--skipped <file>]` (spec §4.1); bare PDF paths are a dev-launch convenience
// for `dotnet run -- file.pdf`. Kept apart from App so it can be unit-tested: App can't be
// constructed under the test host.
public sealed record LaunchArguments(string? ListPath, string? SkippedPath, IReadOnlyList<string> PdfPaths)
{
    private const string ListSwitch = "--list";
    private const string SkippedSwitch = "--skipped";

    // Either switch means a shell-extension launch, so the remaining arguments are never taken as
    // PDF paths — a switch with its value missing must not turn into a file named "--list".
    public static LaunchArguments Parse(string[] args)
    {
        if (!args.Contains(ListSwitch) && !args.Contains(SkippedSwitch))
        {
            return new LaunchArguments(null, null, args);
        }

        return new LaunchArguments(FindSwitchValue(args, ListSwitch), FindSwitchValue(args, SkippedSwitch), []);
    }

    // The shell extension writes these as UTF-8, one path per line, and leaves deleting them to the
    // app. A read failure propagates; only the cleanup is best-effort.
    public static string[] ReadAndDeleteTempFile(string path)
    {
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup — a leftover temp file is untidy but harmless.
        }

        return lines;
    }

    private static string? FindSwitchValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        string value = args[index + 1];
        return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
    }
}
