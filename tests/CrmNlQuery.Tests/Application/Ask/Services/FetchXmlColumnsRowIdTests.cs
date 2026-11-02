using CrmNlQuery.Application.Ask.Services;

namespace CrmNlQuery.Tests.Application.Ask.Services;

/// <summary>
/// Tests row id handling in FetchXML column extraction.
/// </summary>
public class FetchXmlColumnsRowIdTests
{
    /// <summary>
    /// Verifies that the hidden row id is not treated as a carried-over column.
    /// </summary>
    [Fact]
    public void TheHiddenRowId_IsNotACarriedOverColumn()
    {
        var fetch = "<fetch distinct=\"true\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"accountid\" alias=\"_rowid\" /></entity></fetch>";

        Assert.Equal(new[] { "name" }, FetchXmlColumns.Of(fetch).ToArray());
    }
}
