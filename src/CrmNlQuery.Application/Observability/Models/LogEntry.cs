namespace CrmNlQuery.Application.Observability.Models;

/// <summary>
/// Represents one stored application log entry; question text and CRM rows never enter the log.
/// </summary>
public sealed record LogEntry(DateTimeOffset At, int Level, string Message, string? Exception, string? RequestId);
