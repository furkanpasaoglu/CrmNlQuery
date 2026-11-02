namespace CrmNlQuery.Application.Observability.Models;

/// <summary>
/// Describes one health check: name and a function that runs it.
/// </summary>
public sealed record HealthCheck(string Name, Func<CancellationToken, Task> Run);
