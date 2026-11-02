using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CrmNlQuery.Composition;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Eval.Services;
using CrmNlQuery.Infrastructure.External.Crm;

namespace CrmNlQuery.Tests.Architecture;

/// <summary>
/// Tests the Clean Architecture dependency rules, where dependencies only point inward.
/// </summary>
public class ArchitectureTests
{
    private enum Layer { Domain, Application, Infrastructure, Web, Composition, Eval }

    private static Layer? LayerOf(string project)
    {
        if (project == "CrmNlQuery.Domain") return Layer.Domain;
        if (project == "CrmNlQuery.Application") return Layer.Application;
        if (project == "CrmNlQuery.Web") return Layer.Web;
        if (project == "CrmNlQuery.Composition") return Layer.Composition;
        if (project == "CrmNlQuery.Eval") return Layer.Eval;
        if (project == "CrmNlQuery.Infrastructure") return Layer.Infrastructure;
        return null;
    }

    private static IReadOnlySet<Layer> AllowedLayers(Layer layer) => layer switch
    {
        Layer.Domain => new HashSet<Layer>(),
        Layer.Application => new HashSet<Layer> { Layer.Domain },
        Layer.Infrastructure => new HashSet<Layer> { Layer.Application, Layer.Domain },
        Layer.Web => new HashSet<Layer> { Layer.Application, Layer.Domain, Layer.Composition },
        Layer.Composition => new HashSet<Layer> { Layer.Application, Layer.Domain, Layer.Infrastructure },
        Layer.Eval => new HashSet<Layer> { Layer.Application, Layer.Domain, Layer.Composition },
        _ => new HashSet<Layer>(),
    };

    private static IEnumerable<(string Name, XDocument Doc)> SourceProjects()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(RepoPaths.Root(), "src"), "*.csproj", SearchOption.AllDirectories))
        {
            yield return (Path.GetFileNameWithoutExtension(file), XDocument.Load(file));
        }
    }

    private static IEnumerable<string> ProjectReferences(XDocument doc) =>
        doc.Descendants("ProjectReference")
            .Select(e => (string)e.Attribute("Include")!)
            .Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')));

    private static IEnumerable<string> PackageReferences(XDocument doc) =>
        doc.Descendants("PackageReference").Select(e => (string)e.Attribute("Include")!);

    /// <summary>
    /// Verifies that every project in src belongs to a layer.
    /// </summary>
    [Fact]
    public void EveryProjectInSrc_BelongsToALayer()
    {
        var unknown = SourceProjects().Where(p => LayerOf(p.Name) is null).Select(p => p.Name).ToList();

        Assert.True(unknown.Count == 0, "Katmanı belli olmayan proje: " + string.Join(", ", unknown));
    }

    /// <summary>
    /// Verifies that project references point only inwards.
    /// </summary>
    [Fact]
    public void ProjectReferences_PointOnlyInwards()
    {
        var violations = new List<string>();
        foreach (var (name, doc) in SourceProjects())
        {
            var layer = LayerOf(name);
            if (layer is null) continue;
            foreach (var reference in ProjectReferences(doc))
            {
                var target = LayerOf(reference);
                if (target is null || !AllowedLayers(layer.Value).Contains(target.Value))
                {
                    violations.Add($"{name} ({layer}) -> {reference} ({(target?.ToString() ?? "?")})");
                }
            }
        }

        Assert.True(violations.Count == 0, "İzin verilmeyen proje bağımlılıkları:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Verifies that infrastructure technology folders never use each other.
    /// </summary>
    [Fact]
    public void InfrastructureTechnologies_NeverUseEachOther()
    {
        var infrastructure = Path.Combine(RepoPaths.Root(), "src", "CrmNlQuery.Infrastructure");
        var groups = Directory.GetDirectories(infrastructure).Where(d => !Regex.IsMatch(Path.GetFileName(d), "^(bin|obj)$")).ToList();
        Assert.Equal(new[] { "External", "Persistence" }, groups.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Empty(groups.SelectMany(g => Directory.GetFiles(g, "*.cs")));

        var violations = new List<string>();
        foreach (var techDir in groups.SelectMany(Directory.GetDirectories))
        {
            var tech = Path.GetFileName(Path.GetDirectoryName(techDir)) + "." + Path.GetFileName(techDir);
            foreach (var file in Directory.GetFiles(techDir, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepoPaths.Root(), file).Replace('\\', '/');
                if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"CrmNlQuery\.Infrastructure\.(\w+\.\w+)"))
                {
                    if (m.Groups[1].Value != tech)
                    {
                        violations.Add($"{relative}: {m.Value}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, "Infrastructure technologies use each other:\n" + string.Join("\n", violations.Distinct()));
    }

    /// <summary>
    /// Verifies that domain has no package references and application only the logging abstractions.
    /// </summary>
    [Fact]
    public void Domain_HasNoPackageReferences_AndApplication_OnlyTheLoggingAbstractions()
    {
        var projects = SourceProjects().ToDictionary(p => p.Name, p => p.Doc);

        Assert.Empty(PackageReferences(projects["CrmNlQuery.Domain"]));
        Assert.Equal(new[] { "Microsoft.Extensions.Logging.Abstractions" }, PackageReferences(projects["CrmNlQuery.Application"]).ToArray());
    }

    /// <summary>
    /// Returns the compiled assemblies to check, which also catches hidden dependencies not listed in the project files.
    /// </summary>
    public static IEnumerable<object[]> Assemblies() => new[]
    {
        new object[] { typeof(EntityInfo).Assembly },
        new object[] { typeof(AskPipeline).Assembly },
        new object[] { typeof(CrmClient).Assembly },
        new object[] { typeof(PipelineFactory).Assembly },
        new object[] { typeof(Program).Assembly },
        new object[] { typeof(EvalRunner).Assembly },
    };

    /// <summary>
    /// Verifies that compiled assemblies reference only allowed layers.
    /// </summary>
    [Theory]
    [MemberData(nameof(Assemblies))]
    public void CompiledAssemblies_ReferenceOnlyAllowedLayers(Assembly assembly)
    {
        var name = assembly.GetName().Name!;
        var layer = LayerOf(name)!.Value;
        var allowed = AllowedLayers(layer);

        var violations = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("CrmNlQuery.", StringComparison.Ordinal))
            .Where(n => LayerOf(n) is not { } target || !allowed.Contains(target))
            .ToList();

        Assert.True(violations.Count == 0, $"{name} ({layer}) izin verilmeyen derlemelere bağlı: {string.Join(", ", violations)}");
    }

    /// <summary>
    /// Verifies that every public type lives under its assembly namespace.
    /// </summary>
    [Theory]
    [MemberData(nameof(Assemblies))]
    public void EveryPublicType_LivesUnderItsAssemblyNamespace(Assembly assembly)
    {
        var name = assembly.GetName().Name!;

        var misplaced = assembly.GetExportedTypes()
            .Where(t => t.Namespace is not null)
            .Where(t => t.Namespace != name && !t.Namespace!.StartsWith(name + ".", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(misplaced.Count == 0, $"{name} içinde ad alanı uyumsuz türler:\n" + string.Join("\n", misplaced));
    }
}
