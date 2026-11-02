using System.Text.Json;
using System.Xml.Linq;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Eval.Models;
using CrmNlQuery.Eval.Services;

namespace CrmNlQuery.Tests.Eval;

/// <summary>
/// Tests that reference queries in eval JSON files are valid before running against the CRM.
/// </summary>
public class EvalReferenceFilesTests
{
    private static IEnumerable<JsonElement> Cases(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("turns", out var turns))
        {
            foreach (var turn in turns.EnumerateArray()) { yield return turn; }
        }
        else
        {
            yield return item;
        }
    }

    private static IEnumerable<(string File, string Id, JsonElement Expect)> ReferenceCases()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(RepoPaths.Root(), "eval"), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { continue; }
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                foreach (var node in Cases(item))
                {
                    if (!node.TryGetProperty("expect", out var expect) || expect.ValueKind != JsonValueKind.Object || !expect.TryGetProperty("reference", out _)) { continue; }
                    var id = node.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "?" : "?";
                    yield return (Path.GetFileName(path), id, expect.Clone());
                }
            }
        }
    }

    /// <summary>
    /// Verifies that every reference query is well-formed.
    /// </summary>
    [Fact]
    public void EveryReference_IsWellFormed()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var all = ReferenceCases().ToList();
        Assert.NotEmpty(all);
        foreach (var (file, id, expect) in all)
        {
            var reference = JsonSerializer.Deserialize<EvalReference>(expect.GetProperty("reference").GetRawText(), options)!;
            var steps = reference.Steps is { Count: > 0 } s ? s.Select(x => x.FetchXml ?? "").ToList() : new List<string> { reference.FetchXml ?? "" };
            for (var i = 0; i < steps.Count; i++)
            {
                Assert.True(steps[i].Length > 0, file + ":" + id + " has an empty reference");
                var root = XDocument.Parse(steps[i]).Root;
                Assert.NotNull(root);
                Assert.True(root!.Element("entity")?.Attribute("name") is not null, file + ":" + id + " step " + (i + 1) + " has no <entity name>");
                Assert.All(root.Descendants("link-entity"), link => Assert.True(link.Attribute("alias") is not null, file + ":" + id + " has a link-entity without alias"));
                if (i > 0)
                {
                    Assert.True(steps[i].Contains("{{ids}}", StringComparison.Ordinal), file + ":" + id + " step " + (i + 1) + " must use the ids placeholder to receive the previous step ids");
                }
            }
            if (reference.Steps is { Count: > 1 } multi)
            {
                Assert.All(multi.Take(multi.Count - 1), step => Assert.Contains(step.Reduce, new[] { "all", "min", "max" }));
            }
        }
    }

    /// <summary>
    /// Verifies that a reference case does not also keep a hardcoded row count or name.
    /// </summary>
    [Fact]
    public void AReferenceCase_DoesNotAlsoKeepAHardCodedRowCountOrName()
    {
        foreach (var (file, id, expect) in ReferenceCases())
        {
            foreach (var forbidden in new[] { "rowCount", "minRows", "mustContain", "mustNotContain" })
            {
                Assert.False(expect.TryGetProperty(forbidden, out _), file + ":" + id + " must not keep '" + forbidden + "' next to a reference");
            }
        }
    }

    private static ResultTable Aggregate(params string[][] rows)
        => new ResultTable(new[] { "a", "n" }, rows.Select(r => (IReadOnlyList<string>)r).ToList()) { Raw = rows.Select(r => (IReadOnlyList<string>)r).ToList() };

    /// <summary>
    /// Verifies that the reduce helper picks the min or max including ties.
    /// </summary>
    [Fact]
    public void ReduceHelper_PicksMinOrMaxIncludingTies()
    {
        var table = Aggregate(new[] { "a", "1" }, new[] { "b", "3" }, new[] { "c", "1" });

        Assert.Equal(new[] { "a", "c" }, ReferenceCheck.Reduce(table, "min"));
        Assert.Equal(new[] { "b" }, ReferenceCheck.Reduce(table, "max"));
        Assert.Equal(new[] { "a", "b", "c" }, ReferenceCheck.Reduce(table, "all"));
    }

    private static EvalReference TwoSteps(string reduce) => new EvalReference
    {
        Steps = new List<EvalRefStep>
        {
            new EvalRefStep { FetchXml = "<fetch aggregate='true'><entity name='opportunity'><attribute name='customerid' groupby='true' alias='a'/><attribute name='opportunityid' aggregate='count' alias='n'/></entity></fetch>", Reduce = reduce },
            new EvalRefStep { FetchXml = "<fetch><entity name='contact'><attribute name='fullname'/><filter><condition attribute='parentcustomerid' operator='in'>{{ids}}</condition></filter></entity></fetch>" },
        },
    };

    /// <summary>
    /// Verifies that a multi step reference stops when an earlier step finds nothing.
    /// </summary>
    [Fact]
    public async Task AMultiStepReference_StopsWhenAnEarlierStepFindsNothing()
    {
        var calls = 0;
        Task<ResultTable> Run(string fetch, CancellationToken ct)
        {
            calls++;
            return Task.FromResult(Aggregate());
        }

        var (_, table) = await ReferenceCheck.ExecuteAsync(TwoSteps("max"), Run, CancellationToken.None);

        Assert.Empty(table.Rows);
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// Verifies that a multi step reference fills the next step with the reduced IDs.
    /// </summary>
    [Fact]
    public async Task AMultiStepReference_FillsTheNextStepWithTheReducedIds()
    {
        var seen = new List<string>();
        Task<ResultTable> Run(string fetch, CancellationToken ct)
        {
            seen.Add(fetch);
            return Task.FromResult(seen.Count == 1
                ? Aggregate(new[] { "x", "1" }, new[] { "y", "3" }, new[] { "z", "1" })
                : new ResultTable(new[] { "fullname" }, new List<IReadOnlyList<string>> { new[] { "P" } }));
        }

        await ReferenceCheck.ExecuteAsync(TwoSteps("min"), Run, CancellationToken.None);

        Assert.Equal(2, seen.Count);
        Assert.Contains("<value>x</value><value>z</value>", seen[1]);
        Assert.DoesNotContain("<value>y</value>", seen[1]);
        Assert.DoesNotContain("{{ids}}", seen[1]);
    }
}
