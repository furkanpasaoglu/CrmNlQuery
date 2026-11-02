using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Tests record counting against the real lab CRM, comparing two count methods derived from a stored query.
/// </summary>
[Trait("Category", "Live")]
public class RecordCountLiveTests
{
    private const string Prefix = "ZZCOUNT-";
    private const int Count = 600;

    private static CrmClient Client(out CrmOptions options)
    {
        options = LiveMetadataTests.LiveOptions();
        return CrmClient.Create(options);
    }

    /// <summary>
    /// Verifies that both count methods count the same records and the number is the CRM's own.
    /// </summary>
    [Fact]
    public async Task BothWays_CountTheSameRecords_AndTheNumberIsTheCrmsOwn()
    {
        var client = Client(out var options);
        var metadata = new MetadataProvider(client, options.MetadataCacheDir, () => DateTimeOffset.UtcNow);
        var builder = new CountQueryBuilder(new PlanValidator(metadata), metadata);
        var counter = new CrmRecordCounter(client);
        var admin = await UserIdAsync(client, options.Domain + "\\" + options.Username);
        var testuserA = await UserIdAsync(client, options.Domain + "\\testuser_a");
        var created = new List<string>();
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
            var q = built.Queries!;

            foreach (var caller in new[] { Caller.User(admin), Caller.User(testuserA) })
            {
                Assert.Equal(Count, await counter.AggregateCountAsync(q.EntitySetName, q.AggregateFetchXml, caller));
                var total = await counter.TotalCountAsync(q.EntitySetName, q.TotalCountFetchXml, caller);
                Assert.Equal(Count, total.Total);
                Assert.False(total.LimitExceeded);
            }
        }
        finally
        {
            foreach (var id in created)
            {
                await client.SendJsonAsync(HttpMethod.Delete, $"accounts({id})", null, admin);
            }
        }
    }

    /// <summary>
    /// Verifies that a one to many filter join counts distinct accounts not the duplicated rows.
    /// </summary>
    [Fact]
    public async Task AOneToManyFilterJoin_CountsDistinctAccounts_NotTheDuplicatedRows()
    {
        var client = Client(out var options);
        var metadata = new MetadataProvider(client, options.MetadataCacheDir, () => DateTimeOffset.UtcNow);
        var builder = new CountQueryBuilder(new PlanValidator(metadata), metadata);
        var counter = new CrmRecordCounter(client);
        var admin = await UserIdAsync(client, options.Domain + "\\" + options.Username);

        var stored = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" />" +
            "<link-entity name=\"opportunity\" from=\"customerid\" to=\"accountid\" alias=\"o\" link-type=\"inner\" /></entity></fetch>";
        var built = await builder.BuildAsync(stored);
        Assert.True(built.Ok, built.Detail);
        var q = built.Queries!;

        using var opportunities = await client.GetAsync("opportunities?$select=_customerid_value", Caller.User(admin));
        var customers = opportunities.RootElement.GetProperty("value").EnumerateArray().Select(o => o.GetProperty("_customerid_value").GetString()).Distinct().Count();
        var rowCount = opportunities.RootElement.GetProperty("value").GetArrayLength();

        Assert.Equal(customers, await counter.AggregateCountAsync(q.EntitySetName, q.AggregateFetchXml, Caller.User(admin)));
        Assert.Equal(customers, (await counter.TotalCountAsync(q.EntitySetName, q.TotalCountFetchXml, Caller.User(admin))).Total);
        Assert.True(rowCount >= customers);
    }

    private static async Task<Guid> UserIdAsync(CrmClient client, string domainAccount)
    {
        var filter = Uri.EscapeDataString($"domainname eq '{domainAccount}'");
        using var doc = await client.GetAsServiceAsync("systemusers?$select=systemuserid&$filter=" + filter);
        return Guid.Parse(doc.RootElement.GetProperty("value")[0].GetProperty("systemuserid").GetString()!);
    }
}
