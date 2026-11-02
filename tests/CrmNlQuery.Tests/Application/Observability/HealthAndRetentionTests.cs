using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Models;
using CrmNlQuery.Application.Observability.Services;

namespace CrmNlQuery.Tests.Application.Observability;

/// <summary>
/// Tests the health checks and retention cleanup.
/// </summary>
public class HealthAndRetentionTests
{
    /// <summary>
    /// Verifies that every check runs and each result is reported.
    /// </summary>
    [Fact]
    public async Task EveryCheckRuns_AndEachResultIsReported()
    {
        var health = new HealthService(new List<HealthCheck>
        {
            new HealthCheck("crm", _ => Task.CompletedTask),
            new HealthCheck("database", _ => throw new InvalidOperationException("connection refused")),
            new HealthCheck("model", _ => Task.CompletedTask),
        }, TimeSpan.FromSeconds(5));

        var items = await health.CheckAsync();

        Assert.Equal(new[] { "crm", "database", "model" }, items.Select(i => i.Name).ToArray());
        Assert.True(items[0].Ok);
        Assert.False(items[1].Ok);
        Assert.Contains("connection refused", items[1].Detail);
        Assert.True(items[2].Ok);
    }

    /// <summary>
    /// Verifies that a slow check is cut off and reported as timed out.
    /// </summary>
    [Fact]
    public async Task ASlowCheck_IsCutOff_AndReportedAsTimedOut()
    {
        var health = new HealthService(new List<HealthCheck>
        {
            new HealthCheck("slow", async ct => await Task.Delay(TimeSpan.FromSeconds(30), ct)),
        }, TimeSpan.FromMilliseconds(100));

        var items = await health.CheckAsync();

        Assert.False(items[0].Ok);
        Assert.Contains("timed out", items[0].Detail);
    }

    /// <summary>
    /// Verifies that an overlong error message is shortened.
    /// </summary>
    [Fact]
    public async Task AnOverlongErrorMessage_IsShortened()
    {
        var health = new HealthService(new List<HealthCheck>
        {
            new HealthCheck("x", _ => throw new InvalidOperationException(new string('e', 1000))),
        }, TimeSpan.FromSeconds(5));
        var items = await health.CheckAsync();
        Assert.True(items[0].Detail!.Length < 300);
    }

    private sealed class PurgingStore : IMetricsStore
    {
        public DateTimeOffset? Cutoff { get; private set; }
        /// <summary>
        /// Records an AI call in the test metrics store.
        /// </summary>
        public Task RecordAiCallAsync(AiCallRecord record, CancellationToken ct = default) => Task.CompletedTask;
        /// <summary>
        /// Records a request in the test metrics store.
        /// </summary>
        public Task RecordRequestAsync(RequestRecord record, CancellationToken ct = default) => Task.CompletedTask;
        /// <summary>
        /// Returns the metrics summary for a date range from the test store.
        /// </summary>
        public Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>
        /// Deletes records older than the cutoff from the test store.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            Cutoff = cutoff;
            return Task.FromResult(7);
        }
    }

    /// <summary>
    /// Verifies that retention purges records older than the configured days.
    /// </summary>
    [Fact]
    public async Task Retention_PurgesRecordsOlderThanTheConfiguredDays()
    {
        var store = new PurgingStore();
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

        var removed = await MetricsRetention.PurgeOnceAsync(store, 90, () => now);

        Assert.Equal(7, removed);
        Assert.Equal(now.AddDays(-90), store.Cutoff);
    }

    /// <summary>
    /// Verifies that zero or negative retention days means keep everything.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Retention_ZeroOrNegativeDays_MeansKeepEverything(int days)
    {
        var store = new PurgingStore();
        var removed = await MetricsRetention.PurgeOnceAsync(store, days, () => DateTimeOffset.UtcNow);
        Assert.Equal(0, removed);
        Assert.Null(store.Cutoff);
    }
}
