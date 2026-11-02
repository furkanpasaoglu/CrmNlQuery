namespace CrmNlQuery.Domain.Feedback.Entities;

/// <summary>
/// Represents a question the user explicitly agreed to share, with question text and query explanation but no result rows, FetchXML, GUIDs or user name.
/// </summary>
public sealed record FeedbackCandidate(string ResultId, string Question, string Explanation, string? Reason, DateTimeOffset CreatedAt);
