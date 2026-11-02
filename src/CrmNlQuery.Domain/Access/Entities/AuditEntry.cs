namespace CrmNlQuery.Domain.Access.Entities;

/// <summary>
/// One administrative audit-log entry: when, who, which action, the target and a detail text.
/// </summary>
public sealed record AuditEntry(DateTimeOffset At, string Actor, string Action, string Target, string Detail);
