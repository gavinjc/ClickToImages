using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClickToImages.App.Tests;

// A missing or mis-suffixed resource key fails silently at runtime: the control renders blank, or
// GetString returns "". These read the source files directly so the gap shows up as a test failure
// instead. Ported from Bureau.Common's ResourceKeyTests.
public partial class ResourceKeyTests
{
    private static readonly string AppDirectory = FindAppDirectory();

    // Name to value, for tests that format with the .resw's own wording.
    internal static readonly Dictionary<string, string> ReswValues = XDocument
        .Load(Path.Combine(AppDirectory, "Strings", "en-us", "Resources.resw"))
        .Root!.Elements("data")
        .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!);

    private static readonly IReadOnlyCollection<string> ReswKeys = ReswValues.Keys;

    public static TheoryData<string> SourceFiles(string pattern)
    {
        var files = new TheoryData<string>();
        foreach (string path in Directory.GetFiles(AppDirectory, pattern, SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(AppDirectory, path);
            if (!relative.StartsWith("bin" + Path.DirectorySeparatorChar) && !relative.StartsWith("obj" + Path.DirectorySeparatorChar))
            {
                files.Add(relative);
            }
        }

        return files;
    }

    [Theory]
    [MemberData(nameof(SourceFiles), "*.xaml")]
    public void EveryXUid_HasAtLeastOnePropertySuffixedKey(string relativeXamlPath)
    {
        string xaml = File.ReadAllText(Path.Combine(AppDirectory, relativeXamlPath));

        foreach (Match match in XUidPattern().Matches(xaml))
        {
            string uid = match.Groups[1].Value;
            Assert.True(
                ReswKeys.Any(key => key.StartsWith(uid + ".", StringComparison.Ordinal)),
                $"x:Uid=\"{uid}\" in {relativeXamlPath} has no \"{uid}.<Property>\" key in Resources.resw");
        }
    }

    [Theory]
    [MemberData(nameof(SourceFiles), "*.cs")]
    public void EveryGetStringKey_IsInResw(string relativeCodePath)
    {
        string code = File.ReadAllText(Path.Combine(AppDirectory, relativeCodePath));

        foreach (Match match in GetStringPattern().Matches(code))
        {
            string key = match.Groups[1].Value;
            Assert.True(ReswKeys.Contains(key), $"GetString(\"{key}\") in {relativeCodePath} has no key in Resources.resw");
        }
    }

    [GeneratedRegex("x:Uid=\"([^\"]+)\"")]
    private static partial Regex XUidPattern();

    [GeneratedRegex("GetString\\(\"([^\"]+)\"\\)")]
    private static partial Regex GetStringPattern();

    private static string FindAppDirectory()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "ClickToImages.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/ClickToImages.App not found above the test output folder");
    }
}
