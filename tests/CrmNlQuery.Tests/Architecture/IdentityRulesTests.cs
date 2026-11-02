using System.Text.RegularExpressions;
using CrmNlQuery.Tests.Support;

namespace CrmNlQuery.Tests.Architecture;

/// <summary>
/// Holds tests for the rules that no CRM data query runs without an identity.
/// </summary>
public class IdentityRulesTests
{
    private static IEnumerable<(string File, string Text)> Sources(params string[] relativeFolders)
    {
        foreach (var folder in relativeFolders)
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

    /// <summary>
    /// Verifies that web cannot reach the service account paths.
    /// </summary>
    [Fact]
    public void Web_CannotReachTheServiceAccountPaths()
    {
        var violations = Sources(Path.Combine("src", "CrmNlQuery.Web"))
            .Where(f => Regex.IsMatch(f.Text, @"ForEvaluation|GetAsServiceAsync|allowEvaluationCaller"))
            .Select(f => f.File)
            .ToList();

        Assert.True(violations.Count == 0, "Web uses service-account paths: " + string.Join(", ", violations));
    }

    /// <summary>
    /// Verifies that service account reads are called only from the known system readers.
    /// </summary>
    [Fact]
    public void ServiceAccountReads_AreCalledOnlyFromTheKnownSystemReaders()
    {
        var known = new[]
        {
            "CrmClient.cs", "CrmCallerResolver.cs", "CrmUserNames.cs", "CrmUserViews.cs", "CrmLoginProofReader.cs", "CrmUserDirectory.cs", "CrmObjectTypeCodes.cs", "DefaultViewReader.cs", "MetadataProvider.cs", "CrmCreateSchemaProvider.cs", "CrmSetupProbe.cs", "AppComposition.cs",
        };

        var callers = Sources("src")
            .Where(f => Regex.IsMatch(Regex.Replace(f.Text, @"//[^\r\n]*", ""), @"GetAsServiceAsync\("))
            .Select(f => Path.GetFileName(f.File))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Assert.True(callers.All(known.Contains), "New GetAsServiceAsync call site(s): " + string.Join(", ", callers.Except(known)) + ". Add the file here only after checking ServiceReadPolicy.");
    }

    /// <summary>
    /// Verifies that the production composition never enables the evaluation caller.
    /// </summary>
    [Fact]
    public void TheProductionComposition_NeverEnablesTheEvaluationCaller()
    {
        var violations = Sources(Path.Combine("src", "CrmNlQuery.Composition"))
            .Where(f => !f.File.EndsWith("EvalComposition.cs"))
            .Where(f => Regex.IsMatch(Regex.Replace(f.Text, @"//[^\r\n]*", ""), @"evaluation:\s*true|allowEvaluationCaller:\s*true|ForEvaluation"))
            .Select(f => f.File)
            .ToList();

        Assert.True(violations.Count == 0, "Production composition enables the evaluation caller: " + string.Join(", ", violations));
    }

    /// <summary>
    /// Verifies that crm facing ports carry aCaller not an optional guid.
    /// </summary>
    [Fact]
    public void CrmFacingPorts_CarryACaller_NotAnOptionalGuid()
    {
        var violations = Sources(Path.Combine("src", "CrmNlQuery.Application", "Ask", "Abstractions"), Path.Combine("src", "CrmNlQuery.Application", "Counting"))
            .Where(f => Regex.IsMatch(f.Text, @"Guid\?\s+caller", RegexOptions.IgnoreCase))
            .Select(f => f.File)
            .ToList();

        Assert.True(violations.Count == 0, "Ports with an optional caller id: " + string.Join(", ", violations));
    }
}
