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
/// Live test of Open in CRM against the lab CRM, creating a view for a large result and checking explicit order and row count.
/// </summary>
[Trait("Category", "Live")]
public class OpenInCrmLiveTests
{
    private const string Prefix = "ZZVIEW-";
    private const int Count = 600;

    /// <summary>
    /// Verifies that a large result becomes a view with the explicit order and the CRM returns every row.
    /// </summary>
    [Fact]
    public async Task ALargeResult_BecomesAViewWithTheExplicitOrder_AndTheCrmReturnsEveryRow()
    {
        var options = LiveMetadataTests.LiveOptions();
        var client = CrmClient.Create(options);
        var metadata = new MetadataProvider(client, options.MetadataCacheDir, () => DateTimeOffset.UtcNow);
        var builder = new ViewBuilder(new PlanValidator(metadata), metadata);
        var views = new CrmUserViews(client);
        var admin = await UserIdAsync(client, options.Domain + "\\" + options.Username);
        var testuserA = await UserIdAsync(client, options.Domain + "\\testuser_a");
        var created = new List<string>();
        string? viewId = null;
        try
        {
            for (var i = 1; i <= Count; i++)
            {
                using var doc = await client.SendJsonAsync(HttpMethod.Post, "accounts", "{\"name\":\"" + Prefix + i.ToString("D4") + "\"}", admin);
                created.Add(doc!.RootElement.GetProperty("accountid").GetString()!);
            }

            var stored = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><order attribute=\"name\" />" +
                "<filter><condition attribute=\"name\" operator=\"like\" value=\"" + Prefix + "%\" /></filter></entity></fetch>";
            var built = await builder.BuildAsync(stored);
            Assert.True(built.Ok, built.Detail);

            viewId = await views.UpsertAsync(testuserA, OpenInCrmService.ViewName, built.View!);

            using var saved = await client.GetAsync($"userqueries({viewId})?$select=fetchxml,_ownerid_value", Caller.User(testuserA));
            var savedFetch = XDocument.Parse(saved.RootElement.GetProperty("fetchxml").GetString()!).Root!;
            Assert.Null(savedFetch.Attribute("top"));
            Assert.Equal("false", (string?)savedFetch.Descendants("order").Single().Attribute("descending"));
            Assert.Equal(testuserA, Guid.Parse(saved.RootElement.GetProperty("_ownerid_value").GetString()!));

            using var rows = await client.GetAsync("accounts?fetchXml=" + Uri.EscapeDataString(savedFetch.ToString(SaveOptions.DisableFormatting)), Caller.User(testuserA));
            var names = rows.RootElement.GetProperty("value").EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToList();
            Assert.Equal(Count, names.Count);
            Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
        }
        finally
        {
            await views.DeleteAsync(testuserA, OpenInCrmService.ViewName);
            foreach (var id in created)
            {
                await client.SendJsonAsync(HttpMethod.Delete, $"accounts({id})", null, admin);
            }
        }
    }

    private static async Task<Guid> UserIdAsync(CrmClient client, string domainAccount)
    {
        var filter = Uri.EscapeDataString($"domainname eq '{domainAccount}'");
        using var doc = await client.GetAsServiceAsync("systemusers?$select=systemuserid&$filter=" + filter);
        return Guid.Parse(doc.RootElement.GetProperty("value")[0].GetProperty("systemuserid").GetString()!);
    }
}
