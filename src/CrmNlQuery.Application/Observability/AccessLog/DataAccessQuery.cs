namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Query over the data access log: time range, optional user and table filters, newest first.
/// </summary>
public sealed record DataAccessQuery(DateTimeOffset From, DateTimeOffset To, Guid? User, string? Table, int Take = 200)
{
    public string? Source { get; init; }
}
