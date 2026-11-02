using System.Text.RegularExpressions;
using CrmNlQuery.Tests.Support;

namespace CrmNlQuery.Tests.Architecture;

/// <summary>
/// Holds the folder-layout rules of the Clean Architecture structure: one type per file, ports in Abstractions, a technology-free Domain in category folders, and Web endpoint/contract folders.
/// </summary>
public class CleanArchitectureLayoutTests
{
    private static readonly Regex TopLevelType = new(
        @"^(?:(?:public|internal|static|sealed|abstract|partial|readonly|file|ref)\s+)*(?<kind>record\s+struct|record\s+class|class|record|struct|interface|enum|delegate\s+[\w<>?,\[\]. ]+?)\s+(?<name>\w+)",
        RegexOptions.Multiline);

    private static IEnumerable<(string Relative, string Text)> Sources(params string[] top)
    {
        foreach (var folder in top)
        {
            foreach (var file in Directory.GetFiles(Path.Combine(RepoPaths.Root(), folder), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepoPaths.Root(), file).Replace('\\', '/');
                if (relative.Contains("/obj/") || relative.Contains("/bin/"))
                {
                    continue;
                }
                yield return (relative, File.ReadAllText(file));
            }
        }
    }

    private static List<(string Kind, string Name)> Types(string text)
        => TopLevelType.Matches(text).Select(m => (m.Groups["kind"].Value, m.Groups["name"].Value)).ToList();

    /// <summary>
    /// Verifies that every source and test file declares exactly one top-level type whose name matches the file name.
    /// </summary>
    [Fact]
    public void EveryFile_DeclaresOneTopLevelType_NamedLikeTheFile()
    {
        var violations = new List<string>();
        foreach (var (relative, text) in Sources("src", "tests"))
        {
            var fileName = Path.GetFileNameWithoutExtension(relative);
            if (fileName == "Program")
            {
                continue;
            }
            var names = Types(text).Select(t => t.Name).Distinct().ToList();
            if (names.Count != 1 || names[0] != fileName)
            {
                violations.Add($"{relative}: {string.Join(", ", names)}");
            }
        }

        Assert.True(violations.Count == 0, "Files must hold one top-level type named like the file:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Verifies that every Application interface (port) lives in an Abstractions folder.
    /// </summary>
    [Fact]
    public void ApplicationPorts_LiveInAbstractionsFolders()
    {
        var violations = Sources("src/CrmNlQuery.Application")
            .Where(f => Types(f.Text).Any(t => t.Kind == "interface" || t.Kind.StartsWith("delegate", StringComparison.Ordinal)))
            .Where(f => !f.Relative.Split('/').Contains("Abstractions"))
            .Select(f => f.Relative)
            .ToList();

        Assert.True(violations.Count == 0, "Ports outside an Abstractions folder:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Verifies that Domain types are grouped into the Entities, Enums, Exceptions, Constants and Services folders of their area.
    /// </summary>
    [Fact]
    public void DomainTypes_LiveInTheirCategoryFolders()
    {
        var allowed = new HashSet<string> { "Entities", "Enums", "Exceptions", "Constants", "Services" };
        var specialized = new HashSet<string> { "Ask/Steps" };
        var violations = Sources("src/CrmNlQuery.Domain")
            .Select(f => f.Relative.Split('/').Skip(2).ToArray())
            .Where(p => !(p.Length == 3 && (allowed.Contains(p[1]) || specialized.Contains(p[0] + "/" + p[1]))))
            .Select(p => string.Join("/", p))
            .ToList();

        Assert.True(violations.Count == 0, "Domain types outside a category folder:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Verifies that the Domain holds only business concepts: no type named after an external technology and no network, data or framework namespaces.
    /// </summary>
    [Fact]
    public void Domain_KnowsNoTechnology()
    {
        var technology = new Regex("(Crm|Llm|Http|Sql|Postgres|Npgsql|Ollama|Anthropic|Vllm|Prompt)", RegexOptions.IgnoreCase);
        var frameworkUsing = new Regex(@"^using\s+(System\.Net|System\.Data|Microsoft\.)", RegexOptions.Multiline);
        var violations = new List<string>();
        foreach (var (relative, text) in Sources("src/CrmNlQuery.Domain"))
        {
            violations.AddRange(Types(text).Where(t => technology.IsMatch(t.Name)).Select(t => $"{relative}: type {t.Name} names a technology"));
            violations.AddRange(frameworkUsing.Matches(text).Select(m => $"{relative}: {m.Value.Trim()}"));
        }

        Assert.True(violations.Count == 0, "The Domain must not know technologies:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Verifies that Web request and response records live under Contracts and endpoint classes under Endpoints.
    /// </summary>
    [Fact]
    public void WebContractsAndEndpoints_LiveInTheirFolders()
    {
        var violations = new List<string>();
        foreach (var (relative, text) in Sources("src/CrmNlQuery.Web"))
        {
            foreach (var (_, name) in Types(text))
            {
                if (name.EndsWith("Dto", StringComparison.Ordinal) && !relative.StartsWith("src/CrmNlQuery.Web/Contracts/", StringComparison.Ordinal))
                {
                    violations.Add($"{relative}: {name} belongs under Contracts");
                }
                if (name.EndsWith("Endpoints", StringComparison.Ordinal) && !relative.StartsWith("src/CrmNlQuery.Web/Endpoints/", StringComparison.Ordinal))
                {
                    violations.Add($"{relative}: {name} belongs under Endpoints");
                }
            }
        }

        Assert.True(violations.Count == 0, "Web layout:\n" + string.Join("\n", violations));
    }
}
