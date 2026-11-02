namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Holds per-user request usage for the usage report.
/// </summary>
public sealed record UserUsage(Guid User, int Requests);
