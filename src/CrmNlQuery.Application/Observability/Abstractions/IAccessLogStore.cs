using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Application.Observability.Abstractions;

/// <summary>
/// Port for the append-only data-access log store; the only deletion path is retention cleanup, never editing or removing single records.
/// </summary>
public interface IAccessLogStore
{
    /// <summary>
    /// Appends one data-access record to the log.
    /// </summary>
    Task AddAsync(DataAccessRecord record, CancellationToken ct = default);

    /// <summary>
    /// Handles query for access log store.
    /// </summary>
    Task<IReadOnlyList<DataAccessRecord>> QueryAsync(DataAccessQuery query, CancellationToken ct = default);

    /// <summary>
    /// Purges older than for access log store.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
