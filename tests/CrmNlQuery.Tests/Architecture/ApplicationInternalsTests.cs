using System.Text.RegularExpressions;
using CrmNlQuery.Tests.Support;

namespace CrmNlQuery.Tests.Architecture;

/// <summary>
/// Holds architecture tests for the internal layering of the Application Ask area: Services and Models (use case) depend on Steps and Planning, which depend on Abstractions (ports).
/// </summary>
public class ApplicationInternalsTests
{
    private const string UseCase = @"using CrmNlQuery\.Application\.Ask\.(Services|Models);";

    private static IEnumerable<(string File, string Text)> Files(string subfolder)
    {
        var dir = Path.Combine(RepoPaths.Root(), "src", "CrmNlQuery.Application", "Ask", subfolder);
        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            yield return (Path.GetFileName(file), File.ReadAllText(file));
        }
    }

    /// <summary>
    /// Verifies that the Ask ports know neither the planning, the steps nor the use case namespaces.
    /// </summary>
    [Fact]
    public void Abstractions_DoNotKnowThePlanningTheStepsOrTheUseCase()
    {
        var violations = Files("Abstractions")
            .Where(f => Regex.IsMatch(f.Text, @"using CrmNlQuery\.Application\.Ask\.(Planning(\.\w+)*|Steps);") || Regex.IsMatch(f.Text, UseCase))
            .Select(f => f.File)
            .ToList();

        Assert.True(violations.Count == 0, "Abstractions use upper layers: " + string.Join(", ", violations));
    }

    /// <summary>
    /// Verifies that planning does not know the multi-step chain parts.
    /// </summary>
    [Fact]
    public void Planning_DoesNotKnowTheSteps()
    {
        var violations = Files("Planning")
            .Where(f => Regex.IsMatch(f.Text, @"using CrmNlQuery\.Application\.Ask\.Steps;"))
            .Select(f => f.File)
            .ToList();

        Assert.True(violations.Count == 0, "Planning uses the chain parts (Steps): " + string.Join(", ", violations));
    }

    /// <summary>
    /// Verifies that the multi-step chain parts do not know the use case namespaces.
    /// </summary>
    [Fact]
    public void Steps_DoNotKnowTheUseCase()
    {
        var violations = Files("Steps")
            .Where(f => Regex.IsMatch(f.Text, UseCase))
            .Select(f => f.File)
            .ToList();

        Assert.True(violations.Count == 0, "Steps use the use case namespaces: " + string.Join(", ", violations));
    }

    /// <summary>
    /// Verifies that planning knows only the ports, never the use case namespaces.
    /// </summary>
    [Fact]
    public void Planning_KnowsOnlyThePorts_NotTheUseCase()
    {
        var violations = Files("Planning")
            .Where(f => Regex.IsMatch(f.Text, UseCase))
            .Select(f => f.File)
            .ToList();

        Assert.True(violations.Count == 0, "Planning uses the use case namespaces: " + string.Join(", ", violations));
    }
}
