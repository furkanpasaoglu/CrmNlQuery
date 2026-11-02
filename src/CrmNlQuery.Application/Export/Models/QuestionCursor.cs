namespace CrmNlQuery.Application.Export.Models;

/// <summary>
/// Keyset cursor for paging question texts, using a lowercase hex id so ordering is consistent.
/// </summary>
public sealed record QuestionCursor(DateTimeOffset At, string Id);
