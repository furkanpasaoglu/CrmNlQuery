namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Holds one page of chat messages with a cursor for the next page.
/// </summary>
public sealed record ChatMessages(IReadOnlyList<ChatMessage> Items, string? NextCursor);
