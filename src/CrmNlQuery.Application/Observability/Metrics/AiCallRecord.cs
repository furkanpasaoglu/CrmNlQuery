namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Records one model call's duration, tokens, outcome and ids, never row data or question text.
/// </summary>
public sealed record AiCallRecord(Guid RequestId, DateTimeOffset At, string Purpose, string Provider, string Model,
    long DurationMs, int? InputTokens, int? OutputTokens, string Outcome, string? ErrorType,
    int? PromptChars = null, int? EstTokens = null);
