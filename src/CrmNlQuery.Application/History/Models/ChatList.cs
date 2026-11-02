namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Holds one page of chat summaries with a cursor for the next page.
/// </summary>
public sealed record ChatList(IReadOnlyList<ChatSummary> Items, string? NextCursor);
