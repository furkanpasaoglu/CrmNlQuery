using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm;

/// <summary>
/// Live tests of CRM queries against the lab.
/// </summary>
[Trait("Category", "Live")]
public class LiveQueryTests
{
    /// <summary>
    /// Verifies that a products query runs, the default view has columns and account search works.
    /// </summary>
    [Fact]
    public async Task ProductsQuery_Runs_AndDefaultViewHasColumns_AndAccountSearchWorks()
    {
        var options = LiveMetadataTests.LiveOptions();
        var client = CrmClient.Create(options, allowEvaluationCaller: true);
        var executor = new FetchQueryExecutor(client);
        var provider = new MetadataProvider(client, options.MetadataCacheDir, () => DateTimeOffset.UtcNow);

        var rows = await executor.RunAsync("products",
            "<fetch top=\"5\"><entity name=\"product\"><attribute name=\"productid\" /><attribute name=\"name\" /></entity></fetch>", Caller.ForEvaluation());
        Assert.True(rows.GetArrayLength() > 0);

        var columns = await new DefaultViewReader(client).GetDefaultColumnsAsync("product");
        Assert.Contains("name", columns);

        var account = await provider.GetEntityAsync("account");
        var found = await new CrmRecordSearcher(executor).SearchByNameAsync(account!, "Alpine", Caller.ForEvaluation());
        Assert.Contains(found, m => m.Name.Contains("Alpine"));
    }
}
