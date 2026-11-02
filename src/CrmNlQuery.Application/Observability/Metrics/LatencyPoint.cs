namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Holds a latency measurement for the usage report.
/// </summary>
public sealed record LatencyPoint(string Purpose, int Calls, int Errors, double P50Ms, double P95Ms,
    double? AvgPromptChars = null, double? AvgEstTokens = null);
