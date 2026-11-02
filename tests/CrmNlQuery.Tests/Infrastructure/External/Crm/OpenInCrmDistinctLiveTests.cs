using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Application.CrmViews.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm;

/// <summary>
/// Live test that a distinct query with a hidden row id becomes a CRM view returning the same deduplicated result.
/// </summary>
[Trait("Category", "Live")]
public class OpenInCrmDistinctLiveTests
{
    /// <summary>
    /// Verifies that a distinct query with a hidden row ID becomes a view without that column and the CRM still returns no duplicates.
    /// </summary>
    [Fact]
    public async Task ADistinctQueryWithAHiddenRowId_BecomesAViewWithoutThatColumn_AndTheCrmStillReturnsNoDuplicates()
    {
        var options = LiveMetadataTests.LiveOptions();
        var client = CrmClient.Create(options);
        var metadata = new MetadataProvider(client, options.MetadataCacheDir, () => DateTimeOffset.UtcNow);
        var builder = new ViewBuilder(new PlanValidator(metadata), metadata);
        var views = new CrmUserViews(client);
        var admin = await UserIdAsync(client, options.Domain + "\\" + options.Username);
        var stored = "<fetch top=\"500\" distinct=\"true\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"accountid\" alias=\"_rowid\" />" +
            "<order attribute=\"name\" /><link-entity name=\"opportunity\" from=\"customerid\" to=\"accountid\" alias=\"o\" link-type=\"inner\" /></entity></fetch>";
        try
        {
            var built = await builder.BuildAsync(stored);
            Assert.True(built.Ok, built.Detail);
            Assert.Equal(new[] { "name" }, built.View!.Columns.ToArray());

            var viewId = await views.UpsertAsync(admin, OpenInCrmService.ViewName, built.View!);

            using var saved = await client.GetAsync($"userqueries({viewId})?$select=fetchxml,layoutxml", Caller.User(admin));
            var savedFetch = XDocument.Parse(saved.RootElement.GetProperty("fetchxml").GetString()!).Root!;
            Assert.Equal("true", (string?)savedFetch.Attribute("distinct"));
            Assert.DoesNotContain("_rowid", saved.RootElement.GetProperty("layoutxml").GetString()!);

            using var original = await client.GetAsync("accounts?fetchXml=" + Uri.EscapeDataString(stored), Caller.User(admin));
            using var fromView = await client.GetAsync("accounts?fetchXml=" + Uri.EscapeDataString(savedFetch.ToString(SaveOptions.DisableFormatting)), Caller.User(admin));
            var expected = original.RootElement.GetProperty("value").GetArrayLength();
            Assert.True(expected > 0);
            Assert.Equal(expected, fromView.RootElement.GetProperty("value").GetArrayLength());
        }
        finally
        {
            await views.DeleteAsync(admin, OpenInCrmService.ViewName);
        }
    }

    private static async Task<Guid> UserIdAsync(CrmClient client, string domainAccount)
    {
        var filter = Uri.EscapeDataString($"domainname eq '{domainAccount}'");
        using var doc = await client.GetAsServiceAsync("systemusers?$select=systemuserid&$filter=" + filter);
        return Guid.Parse(doc.RootElement.GetProperty("value")[0].GetProperty("systemuserid").GetString()!);
    }
}
