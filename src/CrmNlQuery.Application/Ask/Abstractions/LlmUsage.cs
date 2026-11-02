namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Holds the input and output token counts of a model call, when known.
/// </summary>
public sealed record LlmUsage(int? InputTokens, int? OutputTokens);
