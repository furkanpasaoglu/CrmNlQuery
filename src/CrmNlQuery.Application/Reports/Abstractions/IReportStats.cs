using CrmNlQuery.Application.Reports.Models;

namespace CrmNlQuery.Application.Reports.Abstractions;

/// <summary>
/// Port for daily report usage counters (shown and opened per report name).
/// </summary>
public interface IReportStats
{
    /// <summary>
    /// Adds a shown count for each of the given report names.
    /// </summary>
    Task AddShownAsync(IReadOnlyList<string> names, CancellationToken ct = default);

    /// <summary>
    /// Adds an opened count for the named report.
    /// </summary>
    Task AddOpenedAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Returns per-report totals for the last days including today, most opened first, up to the given number of rows.
    /// </summary>
    Task<IReadOnlyList<ReportStat>> SummaryAsync(int days, int top, CancellationToken ct = default);

    /// <summary>
    /// Purges older than for report stats.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
