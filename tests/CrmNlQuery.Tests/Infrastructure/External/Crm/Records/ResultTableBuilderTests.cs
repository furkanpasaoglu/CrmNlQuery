using System.Text.Json;
using CrmNlQuery.Infrastructure.External.Crm.Records;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Unit tests for the result table builder, which turns a CRM FetchXML response into columns and rows.
/// </summary>
public class ResultTableBuilderTests
{
    private const string FormattedSuffix = "@OData.Community.Display.V1.FormattedValue";

    private static JsonElement Rows(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>
    /// Verifies that build uses formatted value for option sets and ignores extra properties.
    /// </summary>
    [Fact]
    public void Build_UsesFormattedValue_ForOptionSets_AndIgnoresExtraProperties()
    {
        var fetch = "<fetch><entity name=\"product\"><attribute name=\"name\" /><attribute name=\"statecode\" /></entity></fetch>";
        var rows = Rows("{\"value\":[{\"@odata.etag\":\"W/1\",\"productid\":\"x\",\"name\":\"Chair\",\"statecode\":0,\"statecode" + FormattedSuffix + "\":\"Active\"}]}")
            .GetProperty("value");
        var table = ResultTableBuilder.Build(rows, fetch);
        Assert.Equal(new[] { "name", "statecode" }, table.Columns);
        Assert.Equal(new[] { "Chair", "Active" }, table.Rows[0]);
    }

    /// <summary>
    /// Verifies that build on an aggregate keeps the raw values next to the formatted ones.
    /// </summary>
    [Fact]
    public void Build_OnAnAggregate_KeepsTheRawValues_NextToTheFormattedOnes()
    {
        var fetch = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"createdon\" groupby=\"true\" dategrouping=\"month\" alias=\"m\" /><attribute name=\"estimatedvalue\" aggregate=\"sum\" alias=\"total\" /></entity></fetch>";
        var rows = Rows("{\"value\":[{\"m\":3,\"total\":1234.5,\"total" + FormattedSuffix + "\":\"$1,234.50\"}]}").GetProperty("value");

        var table = ResultTableBuilder.Build(rows, fetch);

        Assert.Equal(new[] { "3", "$1,234.50" }, table.Rows[0]);
        Assert.Equal(new[] { "3", "1234.5" }, table.Raw![0]);
    }

    /// <summary>
    /// Verifies that build on a plain query has no raw values.
    /// </summary>
    [Fact]
    public void Build_OnAPlainQuery_HasNoRawValues()
    {
        var fetch = "<fetch><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>";
        var rows = Rows("{\"value\":[{\"name\":\"Chair\"}]}").GetProperty("value");

        Assert.Null(ResultTableBuilder.Build(rows, fetch).Raw);
    }

    /// <summary>
    /// Verifies that build reads lookup from underscore value property.
    /// </summary>
    [Fact]
    public void Build_ReadsLookupFromUnderscoreValueProperty()
    {
        var fetch = "<fetch><entity name=\"contract\"><attribute name=\"customerid\" /></entity></fetch>";
        var rows = Rows("{\"value\":[{\"_customerid_value\":\"11111111-1111-1111-1111-111111111111\",\"_customerid_value" + FormattedSuffix + "\":\"Örnekova Holding A.Ş.\"}]}")
            .GetProperty("value");
        Assert.Equal("Örnekova Holding A.Ş.", ResultTableBuilder.Build(rows, fetch).Rows[0][0]);
    }

    /// <summary>
    /// Verifies that build reads link entity alias property.
    /// </summary>
    [Fact]
    public void Build_ReadsLinkEntityAliasProperty()
    {
        var fetch = "<fetch><entity name=\"contractdetail\"><attribute name=\"title\" />" +
                    "<link-entity name=\"contract\" from=\"contractid\" to=\"contractid\" alias=\"contract\"><attribute name=\"title\" /></link-entity></entity></fetch>";
        var rows = Rows("{\"value\":[{\"title\":\"Line A\",\"contract.title\":\"Contract 1\"}]}").GetProperty("value");
        Assert.Equal(new[] { "Line A", "Contract 1" }, ResultTableBuilder.Build(rows, fetch).Rows[0]);
    }

    /// <summary>
    /// Verifies that build link attribute with own alias falls back to top level alias for aggregates.
    /// </summary>
    [Fact]
    public void Build_LinkAttributeWithOwnAlias_FallsBackToTopLevelAlias_ForAggregates()
    {
        var fetch = "<fetch aggregate=\"true\"><entity name=\"contract\"><attribute name=\"contractid\" alias=\"count\" aggregate=\"count\" />" +
                    "<link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"a\"><attribute name=\"name\" alias=\"accountname\" groupby=\"true\" /></link-entity></entity></fetch>";
        var rows = Rows("{\"value\":[{\"accountname\":\"Örnekova\",\"count\":3}]}").GetProperty("value");
        Assert.Equal(new[] { "3", "Örnekova" }, ResultTableBuilder.Build(rows, fetch).Rows[0]);
    }

    /// <summary>
    /// Verifies that build link attribute without alias never borrows the main entity value.
    /// </summary>
    [Fact]
    public void Build_LinkAttributeWithoutAlias_NeverBorrowsTheMainEntityValue()
    {
        var fetch = "<fetch><entity name=\"contractdetail\"><attribute name=\"title\" />" +
                    "<link-entity name=\"contract\" from=\"contractid\" to=\"contractid\" alias=\"contract\"><attribute name=\"title\" /></link-entity></entity></fetch>";
        var rows = Rows("{\"value\":[{\"title\":\"Line A\"}]}").GetProperty("value");
        Assert.Equal(new[] { "Line A", "" }, ResultTableBuilder.Build(rows, fetch).Rows[0]);
    }

    /// <summary>
    /// Verifies that build null and missing values become empty strings.
    /// </summary>
    [Fact]
    public void Build_NullAndMissingValues_BecomeEmptyStrings()
    {
        var fetch = "<fetch><entity name=\"product\"><attribute name=\"name\" /><attribute name=\"productnumber\" /><attribute name=\"price\" /></entity></fetch>";
        var rows = Rows("{\"value\":[{\"name\":null,\"productnumber\":\"P-1\"}]}").GetProperty("value");
        Assert.Equal(new[] { "", "P-1", "" }, ResultTableBuilder.Build(rows, fetch).Rows[0]);
    }

    /// <summary>
    /// Verifies that build empty result keeps columns and has no rows.
    /// </summary>
    [Fact]
    public void Build_EmptyResult_KeepsColumnsAndHasNoRows()
    {
        var fetch = "<fetch><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>";
        var table = ResultTableBuilder.Build(Rows("{\"value\":[]}").GetProperty("value"), fetch);
        Assert.Equal(new[] { "name" }, table.Columns);
        Assert.Empty(table.Rows);
    }

    /// <summary>
    /// Verifies that build numbers and booleans are text.
    /// </summary>
    [Fact]
    public void Build_NumbersAndBooleans_AreText()
    {
        var fetch = "<fetch aggregate=\"true\"><entity name=\"account\"><attribute name=\"accountid\" alias=\"count\" aggregate=\"count\" /></entity></fetch>";
        var rows = Rows("{\"value\":[{\"count\":19}]}").GetProperty("value");
        Assert.Equal("19", ResultTableBuilder.Build(rows, fetch).Rows[0][0]);
    }
}
