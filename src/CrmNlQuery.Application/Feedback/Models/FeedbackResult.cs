using CrmNlQuery.Domain.Feedback.Entities;

namespace CrmNlQuery.Application.Feedback.Models;

/// <summary>
/// Represents the result of a feedback operation with its status, optional error and state.
/// </summary>
public sealed record FeedbackResult(FeedbackStatus Status, string? Error = null, FeedbackState? State = null);
