using CrmNlQuery.Application.Ask.Steps;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Tests against the real lab CRM that aggregates grouped by lookup return row ids.
/// </summary>
[Trait("Category", "Live")]
public class AggregateRowIdLiveTests
{
    /// <summary>
    /// Verifies that a group by lookup with the row ID alias gives raw GUIDs in the requested order.
    /// </summary>
    [Fact]
    public async Task AGroupByLookupWithTheRowIdAlias_GivesRawGuids_InTheRequestedOrder()
    {
        var client = CrmClient.Create(LiveMetadataTests.LiveOptions(), allowEvaluationCaller: true);
        var executor = new FetchQueryExecutor(client);
        var fetch = "<fetch aggregate=\"true\"><entity name=\"opportunity\">" +
            "<attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />" +
            "<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" />" +
            "<order alias=\"n\" descending=\"true\" /></entity></fetch>";

        var table = await executor.ExecuteAsync("opportunities", fetch, Caller.ForEvaluation());

        Assert.Equal(new[] { "n" }, table.Columns);
        Assert.NotNull(table.RowIds);
        Assert.Equal(table.Rows.Count, table.RowIds!.Count);
        Assert.NotEmpty(table.Rows);
        Assert.All(table.RowIds, id => Assert.True(Guid.TryParse(id, out _), "not a GUID: " + id));
        var counts = table.Rows.Select(r => int.Parse(r[0])).ToList();
        Assert.Equal(counts.OrderByDescending(c => c).ToList(), counts);
    }

    /// <summary>
    /// Verifies that fewest open opportunities with top one keeps every tied account.
    /// </summary>
    [Fact]
    public async Task FewestOpenOpportunities_WithTopOne_KeepsEveryTiedAccount()
    {
        var client = CrmClient.Create(LiveMetadataTests.LiveOptions(), allowEvaluationCaller: true);
        var executor = new FetchQueryExecutor(client);
        var fetch = "<fetch aggregate=\"true\" top=\"1\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />" +
            "<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /><filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter>" +
            "<order alias=\"n\" descending=\"false\" /></entity></fetch>";

        var plan = TieHandling.Prepare(fetch)!;
        var table = await executor.ExecuteAsync("opportunities", plan.FetchXml, Caller.ForEvaluation());
        var outcome = TieHandling.Resolve(plan, table);

        Assert.True(table.Rows.Count > 1, "the widened query must return more than the single top row");
        var minimum = table.Rows.Min(r => int.Parse(r[0]));
        Assert.Equal(table.Rows.Count(r => int.Parse(r[0]) == minimum), outcome.Table.Rows.Count);
        Assert.All(outcome.Table.Rows, r => Assert.Equal(minimum.ToString(), r[0]));
        if (outcome.Table.Rows.Count > 1)
        {
            Assert.NotNull(outcome.Note);
        }
        Assert.Equal(outcome.Table.Rows.Count, outcome.Table.RowIds!.Count);
    }
}
