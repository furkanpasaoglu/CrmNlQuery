namespace CrmNlQuery.Application.Export.Models;

/// <summary>
/// Keyset cursor (time, id) for paging the audit log from oldest to newest during export.
/// </summary>
public sealed record AuditCursor(DateTimeOffset At, long Id);
