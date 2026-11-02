using CrmNlQuery.Domain.History.Entities;

namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Holds a page of conversations with an opaque next cursor.
/// </summary>
public sealed record ConversationPage(IReadOnlyList<Conversation> Items, string? NextCursor);
