namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Holds one day's request totals for the usage report.
/// </summary>
public sealed record DayPoint(DateOnly Day, int Requests, int Failures, int AiCalls, long InputTokens, long OutputTokens);
