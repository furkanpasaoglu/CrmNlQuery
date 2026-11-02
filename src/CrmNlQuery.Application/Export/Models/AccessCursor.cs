namespace CrmNlQuery.Application.Export.Models;

/// <summary>
/// Keyset cursor for paging the data access log during export.
/// </summary>
public sealed record AccessCursor(DateTimeOffset At, Guid Id);
