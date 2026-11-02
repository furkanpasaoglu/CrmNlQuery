namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Records one question request's outcome, duration, knowledge piece count and ids, never question text.
/// </summary>
public sealed record RequestRecord(Guid Id, DateTimeOffset At, Guid? User, long DurationMs, string Outcome, string? ErrorType, int? KnowledgePieces = null,
    bool? FollowUp = null);
