namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Records one CRM data read made on a user's behalf, with metadata only and no question text, FetchXML or values.
/// </summary>
public sealed record DataAccessRecord(Guid Id, DateTimeOffset At, Guid User, string Source, IReadOnlyList<string> Tables, int Rows, string Outcome, Guid RequestId);
