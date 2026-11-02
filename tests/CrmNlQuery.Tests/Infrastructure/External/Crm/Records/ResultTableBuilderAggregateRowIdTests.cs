using System.Text.Json;
using CrmNlQuery.Infrastructure.External.Crm.Records;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Unit tests for aggregate results where a lookup-grouped column arrives as both raw GUID and formatted name; carried ids must be the raw GUID.
/// </summary>
public class ResultTableBuilderAggregateRowIdTests
{
    private const string FormattedSuffix = "@OData.Community.Display.V1.FormattedValue";

    private static JsonElement Rows(string json) => JsonDocument.Parse(json).RootElement.Clone().GetProperty("value");

    private const string Aggregate = "<fetch aggregate=\"true\"><entity name=\"opportunity\">" +
        "<attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />" +
        "<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /></entity></fetch>";

    /// <summary>
    /// Verifies that the row ID is the raw GUID even when the CRM also sends a formatted name.
    /// </summary>
    [Fact]
    public void TheRowId_IsTheRawGuid_EvenWhenTheCrmAlsoSendsAFormattedName()
    {
        var rows = Rows("{\"value\":[{\"n\":3,\"_rowid\":\"37870e9c-c7c7-f111-b4df-00155d01f303\",\"_rowid" + FormattedSuffix + "\":\"Brightwave Appliances Ltd\"}]}");

        var table = ResultTableBuilder.Build(rows, Aggregate);

        Assert.Equal(new[] { "37870e9c-c7c7-f111-b4df-00155d01f303" }, table.RowIds);
        Assert.Equal(new[] { "n" }, table.Columns);
        Assert.Equal("3", table.Rows[0][0]);
    }

}
