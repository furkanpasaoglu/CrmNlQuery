namespace CrmNlQuery.Application.Observability.Models;

/// <summary>
/// Result of one health check with its name, ok flag and message.
/// </summary>
public sealed record HealthItem(string Name, bool Ok, string? Detail, long Ms);
