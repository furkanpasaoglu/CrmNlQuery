using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Application.Common.Exceptions;
using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Tests.Application.Observability.Services;

/// <summary>
/// Tests the admin report service: time window, day clamping, user names and CRM failure tolerance.
/// </summary>
public class AdminServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 17, 15, 30, 0, TimeSpan.Zero);

    private sealed class CapturingStore : IMetricsStore
    {
        public DateTimeOffset? From { get; private set; }
        public DateTimeOffset? To { get; private set; }
        public DateTimeOffset? PurgeCutoff { get; private set; }
        public MetricsSummary Result { get; set; } = new MetricsSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            new List<DayPoint>(), new List<LatencyPoint>(), new List<ModelUsage>(), new List<CountBy>(), new List<CountBy>(), new List<UserUsage>());
        /// <summary>
        /// Captures the AI call record for assertions.
        /// </summary>
        public Task RecordAiCallAsync(AiCallRecord record, CancellationToken ct = default) => Task.CompletedTask;
        /// <summary>
        /// Captures the request record for assertions.
        /// </summary>
        public Task RecordRequestAsync(RequestRecord record, CancellationToken ct = default) => Task.CompletedTask;
        /// <summary>
        /// Returns a canned metrics summary and remembers the requested window.
        /// </summary>
        public Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            From = from;
            To = to;
            return Task.FromResult(Result);
        }
        /// <summary>
        /// Does nothing in this test store.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            PurgeCutoff = cutoff;
            return Task.FromResult(3);
        }
    }

    private sealed class FakeNames : IUserNames
    {
        public bool Throw { get; set; }
        public List<Guid> Asked { get; } = new List<Guid>();
        /// <summary>
        /// Returns canned user names for the given ids.
        /// </summary>
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            if (Throw) throw new CrmException("CRM is down");
            Asked.AddRange(ids);
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(i => i, i => "Name of " + i.ToString("N").Substring(0, 4)));
        }
    }

    private static AdminService Create(CapturingStore store, FakeNames? names = null)
        => new AdminService(store, names ?? new FakeNames(), null, () => Now);

    /// <summary>
    /// Verifies that window covers the last n days including today in UTC.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(1)]
    [InlineData(30)]
    public async Task Window_CoversTheLastNDaysIncludingToday_InUtc(int days)
    {
        var store = new CapturingStore();
        await Create(store).ReportAsync(days);

        Assert.Equal(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero), store.To);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero).AddDays(-days), store.From);
    }

    /// <summary>
    /// Verifies that days are clamped to one through ninety.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(500, 90)]
    public async Task Days_AreClamped_ToOneThroughNinety(int asked, int used)
    {
        var store = new CapturingStore();
        await Create(store).ReportAsync(asked);
        Assert.Equal(used, (int)(store.To!.Value - store.From!.Value).TotalDays);
    }

    /// <summary>
    /// Verifies that top users get their names.
    /// </summary>
    [Fact]
    public async Task TopUsers_GetTheirNames()
    {
        var user = Guid.NewGuid();
        var store = new CapturingStore();
        store.Result = store.Result with { TopUsers = new List<UserUsage> { new UserUsage(user, 4) } };
        var names = new FakeNames();

        var report = await Create(store, names).ReportAsync(7);

        Assert.Equal(new[] { user }, names.Asked.ToArray());
        Assert.StartsWith("Name of", report.Users[0].Name);
    }

    /// <summary>
    /// Verifies that a CRM failure while fetching names does not break the report.
    /// </summary>
    [Fact]
    public async Task ACrmFailureWhileFetchingNames_DoesNotBreakTheReport()
    {
        var store = new CapturingStore();
        store.Result = store.Result with { Requests = 5, TopUsers = new List<UserUsage> { new UserUsage(Guid.NewGuid(), 5) } };

        var report = await Create(store, new FakeNames { Throw = true }).ReportAsync(7);

        Assert.Equal(5, report.Requests);
        Assert.Null(report.Users[0].Name);
    }
}
