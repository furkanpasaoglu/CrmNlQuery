namespace CrmNlQuery.Application.Observability.Calls;

/// <summary>
/// Records the size and duration of one model call without any prompt text.
/// </summary>
public sealed record LlmCallSample(string Purpose, int PromptChars, int EstTokens, int? InputTokens, int? OutputTokens, long Milliseconds, string? Overflow,
    string? System = null, string? User = null, string? Reply = null);
