using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Counting.Models;
using CrmNlQuery.Application.Counting.Services;

namespace CrmNlQuery.Tests.Application.Counting.Services;

/// <summary>
/// Tests count query derivation: the original query is validated first, ordering, columns and paging are dropped, filters and links are kept.
/// </summary>
public class CountQueryBuilderTests
{
    private static CountQueryBuilder Builder()
    {
        var metadata = TestMetadata.Create();
        return new CountQueryBuilder(new PlanValidator(metadata), metadata);
    }

    private const string Accounts =
        "<fetch top=\"500\" distinct=\"true\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"address1_city\" />" +
        "<order attribute=\"name\" descending=\"true\" />" +
        "<filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"London\" /></filter></entity></fetch>";

    private static XElement Root(string fetchXml) => XDocument.Parse(fetchXml).Root!;

    /// <summary>
    /// Verifies that the aggregate query counts distinct primary keys and keeps only the filter.
    /// </summary>
    [Fact]
    public async Task TheAggregateQuery_CountsDistinctPrimaryKeys_AndKeepsOnlyTheFilter()
    {
        var result = await Builder().BuildAsync(Accounts);

        Assert.True(result.Ok);
        var fetch = Root(result.Queries!.AggregateFetchXml);
        Assert.Equal("true", (string?)fetch.Attribute("aggregate"));
        Assert.Null(fetch.Attribute("top"));
        Assert.Null(fetch.Attribute("distinct"));
        var entity = fetch.Element("entity")!;
        var attribute = Assert.Single(entity.Elements("attribute"));
        Assert.Equal("accountid", (string?)attribute.Attribute("name"));
        Assert.Equal("n", (string?)attribute.Attribute("alias"));
        Assert.Equal("countcolumn", (string?)attribute.Attribute("aggregate"));
        Assert.Equal("true", (string?)attribute.Attribute("distinct"));
        Assert.Empty(entity.Elements("order"));
        Assert.Equal("London", (string?)entity.Element("filter")!.Element("condition")!.Attribute("value"));
        Assert.Equal("accounts", result.Queries.EntitySetName);
    }

    /// <summary>
    /// Verifies that the fallback query asks the CRM for the total record count of distinct records.
    /// </summary>
    [Fact]
    public async Task TheFallbackQuery_AsksTheCrmForTheTotalRecordCountOfDistinctRecords()
    {
        var result = await Builder().BuildAsync(Accounts);

        var fetch = Root(result.Queries!.TotalCountFetchXml);
        Assert.Null(fetch.Attribute("aggregate"));
        Assert.Equal("true", (string?)fetch.Attribute("distinct"));
        Assert.Equal("1", (string?)fetch.Attribute("count"));
        Assert.Equal("1", (string?)fetch.Attribute("page"));
        Assert.Equal("true", (string?)fetch.Attribute("returntotalrecordcount"));
        Assert.Null(fetch.Attribute("top"));
        var entity = fetch.Element("entity")!;
        Assert.Equal("accountid", (string?)Assert.Single(entity.Elements("attribute")).Attribute("name"));
        Assert.Empty(entity.Elements("order"));
        Assert.NotNull(entity.Element("filter"));
    }

    /// <summary>
    /// Verifies that links stay as filters but their columns and orders are dropped.
    /// </summary>
    [Fact]
    public async Task Links_StayAsFilters_ButTheirColumnsAndOrdersAreDropped()
    {
        var withLink = "<fetch top=\"500\"><entity name=\"contract\"><attribute name=\"title\" /><order attribute=\"title\" />" +
            "<link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"cust\"><attribute name=\"address1_city\" /><order attribute=\"name\" />" +
            "<filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"Hamburg\" /></filter></link-entity></entity></fetch>";

        var result = await Builder().BuildAsync(withLink);

        foreach (var xml in new[] { result.Queries!.AggregateFetchXml, result.Queries.TotalCountFetchXml })
        {
            var entity = Root(xml).Element("entity")!;
            var link = Assert.Single(entity.Elements("link-entity"));
            Assert.Empty(link.Elements("attribute"));
            Assert.Empty(link.Elements("order"));
            Assert.Equal("Hamburg", (string?)link.Element("filter")!.Element("condition")!.Attribute("value"));
            Assert.Equal("contractid", (string?)Assert.Single(entity.Elements("attribute")).Attribute("name"));
        }
    }

    /// <summary>
    /// Verifies that a one to many filter join is kept so the count is of distinct accounts.
    /// </summary>
    [Fact]
    public async Task AOneToManyFilterJoin_IsKept_SoTheCountIsOfDistinctAccounts()
    {
        var filterJoin = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" />" +
            "<link-entity name=\"contract\" from=\"customerid\" to=\"accountid\" alias=\"c\" link-type=\"inner\" /></entity></fetch>";

        var result = await Builder().BuildAsync(filterJoin);

        Assert.True(result.Ok);
        Assert.Single(Root(result.Queries!.AggregateFetchXml).Descendants("link-entity"));
        Assert.Equal("true", (string?)Root(result.Queries.TotalCountFetchXml).Attribute("distinct"));
    }

    /// <summary>
    /// Verifies that an aggregate query has nothing to count.
    /// </summary>
    [Fact]
    public async Task AnAggregateQuery_HasNothingToCount()
    {
        var aggregate = "<fetch aggregate=\"true\"><entity name=\"account\"><attribute name=\"accountid\" alias=\"n\" aggregate=\"count\" /></entity></fetch>";

        var result = await Builder().BuildAsync(aggregate);

        Assert.False(result.Ok);
        Assert.Equal(CountRejection.Aggregate, result.Rejection);
    }

    /// <summary>
    /// Verifies that queries the validator rejects produce no count query.
    /// </summary>
    [Theory]
    [InlineData("<fetch top=\"500\"><entity name=\"account\"><attribute name=\"nosuchcolumn\" /></entity></fetch>")]
    [InlineData("<fetch top=\"500\"><entity name=\"nosuchentity\"><attribute name=\"name\" /></entity></fetch>")]
    [InlineData("<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><delete /></entity></fetch>")]
    public async Task AQueryTheValidatorRejects_IsNeverCounted(string stored)
    {
        var result = await Builder().BuildAsync(stored);

        Assert.False(result.Ok);
        Assert.Equal(CountRejection.Invalid, result.Rejection);
    }

    /// <summary>
    /// Verifies that broken XML is rejected without throwing.
    /// </summary>
    [Fact]
    public async Task BrokenXml_IsRejectedWithoutThrowing()
    {
        var result = await Builder().BuildAsync("<fetch><entity");

        Assert.False(result.Ok);
        Assert.Equal(CountRejection.Unparseable, result.Rejection);
    }
}
