namespace CrmNlQuery.Domain.Feedback.Entities;

/// <summary>
/// Represents a user's vote on a result plus meta information, never the question text, rows or values.
/// </summary>
public sealed record FeedbackVote(string ResultId, Guid Owner, string Rating, string? Reason, bool Shared, IReadOnlyList<string> Tables, int? Steps, int? DurationMs,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
