using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Application.Reports.Services;

namespace CrmNlQuery.Tests.Contracts.Observability;

/// <summary>
/// Defines the shared behaviors every report stats implementation must pass.
/// </summary>
public abstract class ReportStatsContract
{
    protected abstract IReportStats CreateStats();

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 10);

    /// <summary>
    /// Verifies that shown and opened are counted per report.
    /// </summary>
    [Fact]
    public async Task ShownAndOpenedAreCountedPerReport()
    {
        var stats = CreateStats();
        var a = Unique("Rapor A ");
        var b = Unique("Rapor B ");

        await stats.AddShownAsync(new[] { a, b });
        await stats.AddShownAsync(new[] { a });
        await stats.AddOpenedAsync(a);

        var rows = (await stats.SummaryAsync(7, 1000)).ToDictionary(r => r.Name);
        Assert.Equal(new ReportStat(a, 2, 1), rows[a]);
        Assert.Equal(new ReportStat(b, 1, 0), rows[b]);
    }

    /// <summary>
    /// Verifies that the most opened report comes first and top limits the list.
    /// </summary>
    [Fact]
    public async Task TheMostOpenedReportComesBeforeTheQuietOneAndTopLimitsTheList()
    {
        var stats = CreateStats();
        var quiet = Unique("Sessiz ");
        var busy = Unique("Cok acilan ");
        await stats.AddShownAsync(new[] { quiet, busy });
        for (var i = 0; i < 5; i++) { await stats.AddOpenedAsync(busy); }

        var all = (await stats.SummaryAsync(7, 100000)).Select(r => r.Name).ToList();
        var top = await stats.SummaryAsync(7, 1);

        Assert.True(all.IndexOf(busy) < all.IndexOf(quiet));
        Assert.Single(top);
    }

    [Fact]
    public async Task BlankNamesAreIgnoredAndLongNamesAreCut()
    {
        var stats = CreateStats();
        var longName = Unique("U ") + new string('x', 300);

        await stats.AddShownAsync(new[] { "  ", "", longName });
        await stats.AddOpenedAsync("   ");

        var rows = await stats.SummaryAsync(7, 1000);
        Assert.DoesNotContain(rows, r => r.Name.Trim().Length == 0);
        Assert.Contains(rows, r => r.Name.StartsWith(longName.Substring(0, 12)) && r.Name.Length <= ReportStatName.MaxLength);
    }
}
