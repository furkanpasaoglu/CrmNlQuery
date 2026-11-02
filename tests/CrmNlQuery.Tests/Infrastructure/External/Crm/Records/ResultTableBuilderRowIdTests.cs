using System.Text.Json;
using CrmNlQuery.Infrastructure.External.Crm.Records;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Unit tests for the hidden per-row record id the table builder keeps for open links, without adding a visible column.
/// </summary>
public class ResultTableBuilderRowIdTests
{
    private static JsonElement Rows(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string Fetch = "<fetch distinct=\"true\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"accountid\" alias=\"_rowid\" /></entity></fetch>";

    /// <summary>
    /// Verifies that the hidden row ID is not a column but is kept per row.
    /// </summary>
    [Fact]
    public void TheHiddenRowId_IsNotAColumn_ButIsKeptPerRow()
    {
        var rows = Rows("{\"value\":[{\"name\":\"A\",\"_rowid\":\"id-1\"},{\"name\":\"B\",\"_rowid\":\"id-2\"}]}").GetProperty("value");

        var table = ResultTableBuilder.Build(rows, Fetch);

        Assert.Equal(new[] { "name" }, table.Columns);
        Assert.Equal(new[] { "A" }, table.Rows[0]);
        Assert.Equal(new[] { "id-1", "id-2" }, table.RowIds);
    }

    /// <summary>
    /// Verifies that without a hidden row ID row IDs is null.
    /// </summary>
    [Fact]
    public void WithoutAHiddenRowId_RowIdsIsNull()
    {
        var rows = Rows("{\"value\":[{\"name\":\"A\"}]}").GetProperty("value");

        var table = ResultTableBuilder.Build(rows, "<fetch><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>");

        Assert.Null(table.RowIds);
    }
}
