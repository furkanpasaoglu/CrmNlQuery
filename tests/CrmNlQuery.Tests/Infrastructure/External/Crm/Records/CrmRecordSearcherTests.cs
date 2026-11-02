using System.Text.Json;
using System.Xml.Linq;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.External.Crm.Records;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Tests the CRM record searcher: FetchXML building and result selection.
/// </summary>
public class CrmRecordSearcherTests
{
    private sealed class StubExecutor : IFetchRunner
    {
        private readonly string _json;
        public string? LastFetchXml { get; private set; }
        public Guid? LastCallerId { get; private set; }
        public StubExecutor(string json) { _json = json; }

        /// <summary>
        /// Stub query executor returning canned JSON for the searcher tests.
        /// </summary>
        public Task<JsonElement> RunAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            LastFetchXml = fetchXml;
            LastCallerId = caller.UserIdOrNull();
            return Task.FromResult(JsonDocument.Parse(_json).RootElement.Clone());
        }
    }

    private static string Row(string id, string name) => $"{{\"accountid\":\"{id}\",\"name\":\"{name}\"}}";

    /// <summary>
    /// Verifies that the built FetchXML is always valid XML and carries the like pattern.
    /// </summary>
    [Theory]
    [InlineData("Summit Tire & Auto Supply")]
    [InlineData("O'Brien")]
    [InlineData("50%_[x")]
    [InlineData("<b>x</b>")]
    public void BuildFetchXml_IsAlwaysValidXml_AndCarriesTheLikePattern(string text)
    {
        var xml = CrmRecordSearcher.BuildFetchXml(TestMetadata.Account(), text);
        var condition = XDocument.Parse(xml).Descendants("condition").Single();
        Assert.Equal(NameNormalizer.ToLikePattern(text), condition.Attribute("value")!.Value);
        Assert.Equal("like", condition.Attribute("operator")!.Value);
    }

    private static EntityInfo AccountWithCreatedOn()
    {
        var account = TestMetadata.Account();
        var attributes = account.Attributes.Concat(new[] { new AttributeInfo("createdon", "Created On", "DateTime", new List<OptionInfo>()) }).ToList();
        return account with { Attributes = attributes };
    }

    /// <summary>
    /// Verifies that the FetchXML asks for created-on only when the entity has it.
    /// </summary>
    [Fact]
    public void BuildFetchXml_AsksForCreatedOn_OnlyWhenTheEntityHasIt()
    {
        var with = XDocument.Parse(CrmRecordSearcher.BuildFetchXml(AccountWithCreatedOn(), "x"));
        Assert.Contains(with.Descendants("attribute"), a => (string?)a.Attribute("name") == "createdon");

        var without = XDocument.Parse(CrmRecordSearcher.BuildFetchXml(TestMetadata.Account(), "x"));
        Assert.DoesNotContain(without.Descendants("attribute"), a => (string?)a.Attribute("name") == "createdon");
    }

    /// <summary>
    /// Verifies that the search runs as the caller.
    /// </summary>
    [Fact]
    public async Task Search_RunsAsTheCaller()
    {
        var executor = new StubExecutor("[]");
        var caller = Guid.NewGuid();
        await new CrmRecordSearcher(executor).SearchByNameAsync(TestMetadata.Account(), "Denema", Caller.User(caller));
        Assert.Equal(caller, executor.LastCallerId);
    }

    /// <summary>
    /// Verifies that a search without a caller runs as the service account.
    /// </summary>
    [Fact]
    public async Task Search_WithoutCaller_RunsAsTheServiceAccount()
    {
        var executor = new StubExecutor("[]");
        await new CrmRecordSearcher(executor).SearchByNameAsync(TestMetadata.Account(), "Denema", Caller.ForEvaluation());
        Assert.Null(executor.LastCallerId);
    }

    /// <summary>
    /// Verifies that created-on is read when present and left null otherwise.
    /// </summary>
    [Fact]
    public async Task Search_ReadsCreatedOn_WhenPresent_AndLeavesItNullOtherwise()
    {
        var withDate = "{\"accountid\":\"" + Guid.NewGuid() + "\",\"name\":\"Alpha\",\"createdon\":\"2026-03-12T09:30:00Z\"}";
        var withoutDate = Row(Guid.NewGuid().ToString(), "Beta");
        var searcher = new CrmRecordSearcher(new StubExecutor("[" + withDate + "," + withoutDate + "]"));

        var result = await searcher.SearchByNameAsync(AccountWithCreatedOn(), "a", Caller.ForEvaluation());

        Assert.Equal(new DateTime(2026, 3, 12, 9, 30, 0, DateTimeKind.Utc), result.Single(r => r.Name == "Alpha").CreatedOn);
        Assert.Null(result.Single(r => r.Name == "Beta").CreatedOn);
    }

    /// <summary>
    /// Verifies that the search prefers an exact normalized match.
    /// </summary>
    [Fact]
    public async Task Search_PrefersExactNormalizedMatch()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var json = "[" + Row(id1.ToString(), "Denema Sigorta A.Ş.") + "," + Row(id2.ToString(), "Denema Sağlık Sigorta") + "]";
        var searcher = new CrmRecordSearcher(new StubExecutor(json));

        var result = await searcher.SearchByNameAsync(TestMetadata.Account(), "Denema Sigorta", Caller.ForEvaluation());

        Assert.Single(result);
        Assert.Equal(id1, result[0].Id);
    }

    /// <summary>
    /// Verifies that all candidates are returned when there is no exact match.
    /// </summary>
    [Fact]
    public async Task Search_ReturnsAllCandidates_WhenNoExactMatch()
    {
        var json = "[" + Row(Guid.NewGuid().ToString(), "Denema Sigorta A.Ş.") + "," + Row(Guid.NewGuid().ToString(), "Denema Sağlık Sigorta") + "]";
        var searcher = new CrmRecordSearcher(new StubExecutor(json));
        var result = await searcher.SearchByNameAsync(TestMetadata.Account(), "Denema", Caller.ForEvaluation());
        Assert.Equal(2, result.Count);
    }

    /// <summary>
    /// Verifies that no rows returns an empty result.
    /// </summary>
    [Fact]
    public async Task Search_NoRows_ReturnsEmpty()
    {
        var searcher = new CrmRecordSearcher(new StubExecutor("[]"));
        Assert.Empty(await searcher.SearchByNameAsync(TestMetadata.Account(), "Zorlu", Caller.ForEvaluation()));
    }
}
