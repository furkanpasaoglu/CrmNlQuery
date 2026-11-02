namespace CrmNlQuery.Domain.History.Entities;

/// <summary>
/// Represents a chat session: a user's titled group of ordered question-answer records.
/// </summary>
public sealed record Conversation(string Id, Guid Owner, string Title, bool Pinned, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
