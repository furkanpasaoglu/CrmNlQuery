using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Application.Observability.Abstractions;

/// <summary>
/// Port for storing model call and request metrics and reading summaries.
/// </summary>
public interface IMetricsStore
{
    /// <summary>
    /// Records the metrics of one model call, such as step, duration and token counts, never the prompt or question text.
    /// </summary>
    Task RecordAiCallAsync(AiCallRecord record, CancellationToken ct = default);

    /// <summary>
    /// Records the metrics of one request, such as duration and outcome, never the question text.
    /// </summary>
    Task RecordRequestAsync(RequestRecord record, CancellationToken ct = default);

    /// <summary>
    /// Returns a metrics summary for the period from inclusive to exclusive, with UTC day boundaries.
    /// </summary>
    Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// Deletes all records older than the given time and returns how many were deleted.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
