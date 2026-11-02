using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Application.Observability.Abstractions;

/// <summary>
/// Port for the application log store used by the admin Logs page.
/// </summary>
public interface ILogStore
{
    /// <summary>
    /// Appends a batch of log entries to the log store.
    /// </summary>
    Task AddAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct = default);
    /// <summary>
    /// Returns log entries matching the query, newest first.
    /// </summary>
    Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken ct = default);
    /// <summary>
    /// Purges older than for log store.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
