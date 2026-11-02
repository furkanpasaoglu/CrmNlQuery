namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Admin access-log row: the record plus the user name resolved from the CRM, or null if unresolved.
/// </summary>
public sealed record AccessLogRow(Guid Id, DateTimeOffset At, Guid User, string? UserName, string Source, IReadOnlyList<string> Tables, int Rows, string Outcome, Guid RequestId);
