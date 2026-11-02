using System.Text.RegularExpressions;
using CrmNlQuery.Tests.Support;

namespace CrmNlQuery.Tests.Architecture;

/// <summary>
/// Tests that source folders match their namespaces and are not hidden from git.
/// </summary>
public class FolderStructureTests
{
    private static IEnumerable<string> SourceFiles(string top)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(RepoPaths.Root(), top), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepoPaths.Root(), file).Replace('\\', '/');
            if (relative.Contains("/bin/") || relative.Contains("/obj/") || relative.Contains("/logs/"))
            {
                continue;
            }
            yield return relative;
        }
    }

    private static string ExpectedNamespace(string relativePath)
    {
        var parts = relativePath.Split('/');
        var segments = new List<string> { parts[1] };
        segments.AddRange(parts.Skip(2).Take(parts.Length - 3));
        return string.Join(".", segments);
    }

    /// <summary>
    /// Verifies that every file declares the namespace that its folder implies.
    /// </summary>
    [Fact]
    public void EveryFile_DeclaresTheNamespaceThatItsFolderImplies()
    {
        var violations = new List<string>();
        foreach (var file in SourceFiles("src").Concat(SourceFiles("tests")))
        {
            var text = File.ReadAllText(Path.Combine(RepoPaths.Root(), file));
            var match = Regex.Match(text, @"^namespace\s+([\w.]+);", RegexOptions.Multiline);
            if (!match.Success)
            {
                continue;
            }
            var expected = ExpectedNamespace(file);
            if (match.Groups[1].Value != expected)
            {
                violations.Add($"{file}: namespace {match.Groups[1].Value}, beklenen {expected}");
            }
        }

        Assert.True(violations.Count == 0, "Ad alanı klasörle uyumsuz:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Verifies that no source folder is hidden from git.
    /// </summary>
    [Fact]
    public void NoSourceFolder_IsHiddenFromGit()
    {
        var risky = Directory.GetDirectories(Path.Combine(RepoPaths.Root(), "src"), "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(RepoPaths.Root(), d).Replace('\\', '/'))
            .Where(d => !d.Contains("/bin") && !d.Contains("/obj") && !d.Contains("/wwwroot") && !d.Contains("/logs") && !d.Contains("/metadata-cache") && !d.Contains("/Properties"))
            .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^(logs|bin|obj)$", RegexOptions.IgnoreCase))
            .ToList();

        Assert.True(risky.Count == 0, "Yok sayılan adlı kaynak klasörleri: " + string.Join(", ", risky));
    }
}
