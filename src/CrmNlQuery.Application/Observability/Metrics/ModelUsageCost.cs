namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Carries usage and cost totals per model and provider.
/// </summary>
public sealed record ModelUsageCost(string Provider, string Model, int Calls, long InputTokens, long OutputTokens, double? EstimatedCostUsd);
