namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Holds per-model call and token usage for the usage report.
/// </summary>
public sealed record ModelUsage(string Provider, string Model, int Calls, long InputTokens, long OutputTokens);
