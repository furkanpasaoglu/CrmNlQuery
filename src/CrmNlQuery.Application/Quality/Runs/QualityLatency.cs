namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Holds average, p95 and maximum latency of a quality run.
/// </summary>
public sealed record QualityLatency(int AverageMs, int P95Ms, int MaxMs);
