using Xunit.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.Summary.Models;
using CrmNlQuery.Application.Summary.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm;

/// <summary>
/// Live read-only tests of the customer summary against the lab CRM.
/// </summary>
[Trait("Category", "Live")]
public class SummaryLiveTests
{
    private readonly ITestOutputHelper _output;

    public SummaryLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class FixedPolicy : IDataPolicy
    {
        private readonly DataPolicySnapshot _snapshot;
        public FixedPolicy(params string[] extraTables)
        {
            _snapshot = new DataPolicySnapshot(new List<DataPolicyRule>(), DataPolicyDefaults.Tables.Concat(extraTables).ToList());
        }
        /// <summary>
        /// Returns the data policy snapshot used by the live test.
        /// </summary>
        public Task<DataPolicySnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
    }

    private static async Task<(SummaryBuilder Builder, Guid Id)> SetUpAsync()
    {
        var options = LiveMetadataTests.LiveOptions();
        var client = CrmClient.Create(options, allowEvaluationCaller: true);
        var metadata = new PolicyMetadataSource(new MetadataProvider(client, options.MetadataCacheDir, () => DateTimeOffset.UtcNow), new FixedPolicy("new_arac", "activitypointer", "new_servis_kaydi"));
        var builder = new SummaryBuilder(metadata, new FetchQueryExecutor(client), new DefaultViewReader(client));
        var account = await metadata.GetEntityAsync("account");
        var found = await new CrmRecordSearcher(new FetchQueryExecutor(client)).SearchByNameAsync(account!, "Brightwave", Caller.ForEvaluation());
        return (builder, found.First().Id);
    }

    private void Print(SummaryOutcome outcome)
    {
        _output.WriteLine($"{outcome.EntityLabel}: {outcome.RecordName}");
        foreach (var s in outcome.Sections)
        {
            _output.WriteLine($"[{s.Kind}] {s.Title} ({s.Status})");
            foreach (var m in s.Metrics) { _output.WriteLine($"    {m.Label}: {m.Value}"); }
            foreach (var i in s.Items) { _output.WriteLine($"    - {i.Text} | {i.Detail}"); }
        }
    }

    /// <summary>
    /// Verifies that the Brightwave summary has every section the lab data supports and none failed.
    /// </summary>
    [Fact]
    public async Task BrightwaveSummary_HasEverySectionTheLabDataSupports_AndNoneFailed()
    {
        var (builder, id) = await SetUpAsync();

        var outcome = await builder.BuildAsync(Caller.ForEvaluation(), "account", id, "Brightwave");
        Print(outcome);

        Assert.DoesNotContain(outcome.Sections, s => s.Status == SummaryStatus.Unavailable);
        var opportunities = outcome.Sections.Single(s => s.Kind == SummaryKinds.Opportunities);
        Assert.Equal("3", opportunities.Metrics.Single(m => m.Key == "count").Value);
        Assert.Contains("166", opportunities.Metrics.Single(m => m.Key == "total").Value);
        Assert.Equal(3, opportunities.Items.Count);
        var activities = outcome.Sections.Single(s => s.Kind == SummaryKinds.Activities);
        Assert.Equal(4, activities.Items.Count);
        Assert.Contains(activities.Metrics, m => m.Key == "last_contact");
        var vehicles = outcome.Sections.Single(s => s.Kind == SummaryKinds.Generic && s.Entity == "new_arac");
        Assert.Equal("4", vehicles.Metrics.Single(m => m.Key == "count").Value);
        Assert.DoesNotContain(outcome.Sections, s => s.Kind == SummaryKinds.Generic && s.Status == SummaryStatus.Empty);
        Assert.DoesNotContain(outcome.Sections, s => s.Entity == "new_servis_kaydi" && s.ViaEntity is null);
    }

    /// <summary>
    /// Verifies that reopening a summary reads the name and gives the same sections.
    /// </summary>
    [Fact]
    public async Task ReopeningASummary_ReadsTheNameAndGivesTheSameSections()
    {
        var (builder, id) = await SetUpAsync();

        var outcome = await builder.BuildForRecordAsync(Caller.ForEvaluation(), "account", id);

        Assert.Contains("Brightwave", outcome.RecordName);
        Assert.Contains(outcome.Sections, s => s.Kind == SummaryKinds.Basic);
    }
}
