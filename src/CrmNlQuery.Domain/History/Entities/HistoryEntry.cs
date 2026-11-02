namespace CrmNlQuery.Domain.History.Entities;

/// <summary>
/// Represents one stored question-answer record in a chat, including its conversation, parent link and kind.
/// </summary>
public sealed record HistoryEntry(string Id, Guid Owner, string Question, string Entity, string FetchXml,
    DateTimeOffset CreatedAt, string? Title, bool Starred, string? ParentId = null, string? ConversationId = null,
    int? Steps = null, int? DurationMs = null, string? Kind = null, string? Payload = null);
