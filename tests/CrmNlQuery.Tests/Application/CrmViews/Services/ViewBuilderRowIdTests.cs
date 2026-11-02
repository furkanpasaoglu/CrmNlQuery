using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.CrmViews.Services;

namespace CrmNlQuery.Tests.Application.CrmViews.Services;

/// <summary>
/// Tests that the hidden row id column is excluded from view columns while distinct is preserved.
/// </summary>
public class ViewBuilderRowIdTests
{
    /// <summary>
    /// Verifies that the hidden row ID is not a view column and distinct survives.
    /// </summary>
    [Fact]
    public async Task TheHiddenRowId_IsNotAViewColumn_AndDistinctSurvives()
    {
        var metadata = TestMetadata.Create();
        var builder = new ViewBuilder(new PlanValidator(metadata), metadata);
        var fetch = "<fetch top=\"500\" distinct=\"true\"><entity name=\"contract\"><attribute name=\"title\" /><attribute name=\"contractid\" alias=\"_rowid\" />" +
            "<link-entity name=\"contractdetail\" from=\"contractid\" to=\"contractid\" alias=\"d\"><filter><condition attribute=\"price\" operator=\"gt\" value=\"100\" /></filter></link-entity></entity></fetch>";

        var result = await builder.BuildAsync(fetch);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "title" }, result.View!.Columns.ToArray());
        Assert.Equal("true", (string?)XDocument.Parse(result.View.FetchXml).Root!.Attribute("distinct"));
    }
}
