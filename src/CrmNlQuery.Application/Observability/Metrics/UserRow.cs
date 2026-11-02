namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Carries one user's request count for the admin report.
/// </summary>
public sealed record UserRow(Guid User, string? Name, int Requests);
