namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Represents the chat context of one question: which chat it belongs to (null for a new chat) and the follow-up context.
/// </summary>
public sealed record ChatTurn(string? ChatId, ConversationPrevious? Previous);
