using System.Text.RegularExpressions;

namespace ClickToImages.Core.Tests;

// The C++ and C# lists sit on either side of a language boundary, so nothing at build time keeps
// them equal. The C++ one can't be loaded from C#, so it is read as source text; the C# one is the
// compiled list. A mismatch means the menu shows for files the app then rejects, or hides for
// files it would accept.
public class SupportedExtensionsSyncTests
{
    [Fact]
    public void ShellExtensionAndCoreListTheSameExtensions()
    {
        string[] cppExtensions = ReadShellExtensionList();

        Assert.NotEmpty(cppExtensions);
        Assert.Equal(
            cppExtensions.OrderBy(x => x, StringComparer.Ordinal),
            SupportedExtensions.All.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static string[] ReadShellExtensionList()
    {
        string path = Path.Combine(FindRepoRoot(), "src", "ClickToImages.ShellExtension", "SupportedExtensions.h");
        string source = File.ReadAllText(path);

        Match arrayLiteral = Regex.Match(
            source, @"std::array<std::wstring_view,\s*(?<size>\d+)>\s*SupportedExtensions\s*=\s*\{(?<items>[^}]*)\}");
        Assert.True(arrayLiteral.Success, $"Could not find the SupportedExtensions array literal in {path}");

        // A commented-out entry (// L".tif",) isn't compiled, so it mustn't count as listed.
        string items = Regex.Replace(arrayLiteral.Groups["items"].Value, "//.*", "");
        string[] extensions = Regex.Matches(items, "L\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        // std::array accepts fewer initialisers than its size and pads with empty strings, so a
        // stale size compiles; it has to agree with the entries actually listed.
        Assert.Equal(int.Parse(arrayLiteral.Groups["size"].Value), extensions.Length);
        return extensions;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClickToImages.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
