namespace CrmNlQuery.Domain.Feedback.Entities;

/// <summary>
/// Represents the vote state the UI shows: rating, reason and whether it was shared.
/// </summary>
public sealed record FeedbackState(string Rating, string? Reason, bool Shared);
