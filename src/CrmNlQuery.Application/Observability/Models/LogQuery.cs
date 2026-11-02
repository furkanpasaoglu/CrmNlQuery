namespace CrmNlQuery.Application.Observability.Models;

/// <summary>
/// Describes a log query: minimum level, text search, time range and count.
/// </summary>
public sealed record LogQuery(int MinLevel, string? Text, DateTimeOffset? From, int Take);
