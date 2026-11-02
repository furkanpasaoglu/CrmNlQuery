using CrmNlQuery.Tests.Contracts.Observability;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Observability;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Observability;

/// <summary>
/// Tests the in-memory report stats implementation, including windowed old days and purging.
/// </summary>
public class MemoryReportStatsTests : ReportStatsContract
{
    protected override IReportStats CreateStats() => new MemoryReportStats();

    /// <summary>
    /// Verifies that old days are counted only inside the window and can be purged.
    /// </summary>
    [Fact]
    public async Task OldDaysAreCountedOnlyInsideTheWindowAndCanBePurged()
    {
        var now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        var stats = new MemoryReportStats(() => now);
        await stats.AddShownAsync(new[] { "Eski" });
        now = now.AddDays(10);
        await stats.AddShownAsync(new[] { "Yeni" });

        var week = await stats.SummaryAsync(7, 100);
        var purged = await stats.PurgeOlderThanAsync(now.AddDays(-5));

        Assert.Equal("Yeni", Assert.Single(week).Name);
        Assert.Equal(1, purged);
    }
}
