using System.Text.Json;
using CrmNlQuery.Eval.Suites;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Eval.Models;

namespace CrmNlQuery.Tests.Eval.Suites;

/// <summary>
/// Tests the starter set command, which collects facts from a fake CRM and writes a case file the eval model can read.
/// </summary>
public class StarterSetCommandTests
{
    private sealed class FakeSchema : IMetadataSource
    {
        private readonly EntityInfo _incident = new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
            new List<AttributeInfo>
            {
                new AttributeInfo("incidentid", "Case", "Uniqueidentifier", new List<OptionInfo>()),
                new AttributeInfo("title", "Case Title", "String", new List<OptionInfo>()),
                new AttributeInfo("prioritycode", "Priority", "Picklist", new List<OptionInfo> { new OptionInfo(1, "High"), new OptionInfo(2, "Normal"), new OptionInfo(3, "Low") }),
                new AttributeInfo("statecode", "Status", "State", new List<OptionInfo> { new OptionInfo(0, "Active"), new OptionInfo(1, "Resolved") }),
                new AttributeInfo("estimatedvalue", "Estimated Value", "Money", new List<OptionInfo>()),
                new AttributeInfo("customerid", "Customer", "Customer", new List<OptionInfo>()),
            },
            new List<RelationshipInfo> { new RelationshipInfo("incident_customer", "incident", "customerid", "account", "accountid") });

        /// <summary>
        /// Fake schema source serving canned entities.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntitySummary>>(new List<EntitySummary> { new EntitySummary("incident", "incidents", "Case"), new EntitySummary("systemuser", "systemusers", "User") });

        /// <summary>
        /// Returns one canned entity or null if unknown.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default)
            => Task.FromResult<EntityInfo?>(logicalName == "incident" ? _incident : null);

        /// <summary>
        /// Does nothing in the fake, standing in for a schema refresh.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ResultTable Raw(params string[][] rows)
        => new ResultTable(new[] { "a", "b" }, rows.Select(r => (IReadOnlyList<string>)r).ToList()) { Raw = rows.Select(r => (IReadOnlyList<string>)r).ToList() };

    private static Task<ResultTable> Run(string fetch, CancellationToken ct)
    {
        if (fetch.Contains("groupby='true' alias='g'") && fetch.Contains("'prioritycode'")) { return Task.FromResult(Raw(new[] { "1", "5" }, new[] { "2", "30" }, new[] { "3", "10" })); }
        if (fetch.Contains("groupby='true' alias='g'") && fetch.Contains("'statecode'")) { return Task.FromResult(Raw(new[] { "0", "42" }, new[] { "1", "3" })); }
        if (fetch.Contains("groupby='true' alias='g'") && fetch.Contains("'customerid'")) { return Task.FromResult(Raw(Enumerable.Range(0, 12).Select(i => new[] { "id" + i, "3" }).ToArray())); }
        if (fetch.Contains("operator='not-null'")) { return Task.FromResult(Raw(new[] { "40" })); }
        return Task.FromResult(Raw(new[] { "45" }));
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "starter-" + Guid.NewGuid().ToString("N") + ".json");

    /// <summary>
    /// Verifies that generating writes unverified cases that the eval model can read.
    /// </summary>
    [Fact]
    public async Task Generate_WritesUnverifiedCases_ThatTheEvalModelCanRead()
    {
        var path = TempFile();
        try
        {
            Assert.Equal(0, await StarterSetCommand.GenerateAsync(new FakeSchema(), Run, null, "en", path, 6, CancellationToken.None));

            var cases = JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.True(cases.Count >= 6);
            Assert.All(cases, c =>
            {
                Assert.True(c.Generated);
                Assert.NotEqual(true, c.Verified);
                Assert.NotNull(c.Expect.Reference);
                Assert.Equal("incident", c.Expect.Entity);
            });
            Assert.Contains(cases, c => c.Template == "breakdown" && c.Expect.Reference!.Compare!.Columns == "last");
            Assert.Contains(cases, c => c.Template == "top" && c.Answers!["columns"] == "custom:title, estimatedvalue");
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies that only the asked entities are used and an unknown one is skipped.
    /// </summary>
    [Fact]
    public async Task OnlyAskedEntitiesAreUsed_AndAnUnknownOneIsSkipped()
    {
        var path = TempFile();
        try
        {
            await StarterSetCommand.GenerateAsync(new FakeSchema(), Run, new[] { "incident", "nosuchtable" }, "tr", path, 6, CancellationToken.None);

            var text = File.ReadAllText(path);
            Assert.Contains("Case kayıt sayısı kaç?", text);
            Assert.DoesNotContain("nosuchtable", text);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies that verify marks the chosen cases and leaves the rest.
    /// </summary>
    [Fact]
    public async Task Verify_MarksTheChosenCases_AndLeavesTheRest()
    {
        var path = TempFile();
        try
        {
            await StarterSetCommand.GenerateAsync(new FakeSchema(), Run, null, "en", path, 6, CancellationToken.None);
            var first = JsonDocument.Parse(File.ReadAllText(path)).RootElement[0].GetProperty("id").GetString()!;

            StarterSetCommand.Verify(path, first);

            var cases = JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.True(cases.Single(c => c.Id == first).Verified);
            Assert.All(cases.Where(c => c.Id != first), c => Assert.NotEqual(true, c.Verified));

            StarterSetCommand.Verify(path, "all");
            Assert.All(JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!, c => Assert.True(c.Verified));
            Assert.Equal(0, StarterSetCommand.List(path));
        }
        finally { File.Delete(path); }
    }
}
