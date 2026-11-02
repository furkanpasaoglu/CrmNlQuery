using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Unit tests for the default view reader that discovers a table's default view columns.
/// </summary>
public class DefaultViewReaderTests
{
    private const string Layout =
        "<grid name=\"resultset\" object=\"1024\" jump=\"name\"><row name=\"result\" id=\"productid\">" +
        "<cell name=\"name\" width=\"300\" /><cell name=\"productnumber\" width=\"100\" />" +
        "<cell name=\"a_abc.name\" width=\"100\" /></row></grid>";

    /// <summary>
    /// Verifies that parse layout returns cell names skipping linked entity cells.
    /// </summary>
    [Fact]
    public void ParseLayout_ReturnsCellNames_SkippingLinkedEntityCells()
    {
        Assert.Equal(new[] { "name", "productnumber" }, DefaultViewReader.ParseLayout(Layout));
    }

    /// <summary>
    /// Verifies that parse layout keeps attributes that contain underscore.
    /// </summary>
    [Fact]
    public void ParseLayout_KeepsAttributesThatContainUnderscore()
    {
        var layout = "<grid><row><cell name=\"address1_city\" /><cell name=\"x.y\" /></row></grid>";
        Assert.Equal(new[] { "address1_city" }, DefaultViewReader.ParseLayout(layout));
    }

    /// <summary>
    /// Verifies that parse layout empty or broken returns empty.
    /// </summary>
    [Fact]
    public void ParseLayout_EmptyOrBroken_ReturnsEmpty()
    {
        Assert.Empty(DefaultViewReader.ParseLayout(""));
        Assert.Empty(DefaultViewReader.ParseLayout("<grid>"));
    }

    /// <summary>
    /// Verifies that get default columns reads layout from saved query.
    /// </summary>
    [Fact]
    public async Task GetDefaultColumns_ReadsLayoutFromSavedQuery()
    {
        var json = "{\"value\":[{\"layoutxml\":" + System.Text.Json.JsonSerializer.Serialize(Layout) + "}]}";
        var handler = new FakeHandler(_ => FakeHandler.Json(json));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });

        var columns = await new DefaultViewReader(client).GetDefaultColumnsAsync("product");

        Assert.Equal(new[] { "name", "productnumber" }, columns);
        Assert.Contains("savedqueries", handler.Urls[0]);
    }

    /// <summary>
    /// Verifies that get default columns rejects unsafe entity name without calling CRM.
    /// </summary>
    [Fact]
    public async Task GetDefaultColumns_RejectsUnsafeEntityName_WithoutCallingCrm()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"value":[]}"""));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });
        Assert.Empty(await new DefaultViewReader(client).GetDefaultColumnsAsync("x' or 1 eq 1 or 'a"));
        Assert.Empty(handler.Urls);
    }
}
