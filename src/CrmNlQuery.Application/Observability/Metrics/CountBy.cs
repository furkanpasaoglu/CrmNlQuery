namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Holds a label with a count for the usage report.
/// </summary>
public sealed record CountBy(string Key, int Count);
