namespace CrmNlQuery.Application.Feedback.Models;

/// <summary>
/// Represents a count for a feedback key such as a reason.
/// </summary>
public sealed record FeedbackCount(string Key, int Count);
