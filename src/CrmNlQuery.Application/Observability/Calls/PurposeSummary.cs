namespace CrmNlQuery.Application.Observability.Calls;

/// <summary>
/// Summarizes the model calls of one purpose: count, average and maximum estimated tokens and timing.
/// </summary>
public sealed record PurposeSummary(string Purpose, int Calls, double AvgEstTokens, int MaxEstTokens, double AvgInputTokens, double AvgMilliseconds);
