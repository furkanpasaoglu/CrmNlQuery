namespace CrmNlQuery.Application.Feedback.Models;

/// <summary>
/// Represents the up and down vote counts for one day.
/// </summary>
public sealed record FeedbackDay(DateOnly Day, int Up, int Down);
