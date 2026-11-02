namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Holds a model reply text and its token usage.
/// </summary>
public sealed record LlmResult(string Text, LlmUsage? Usage);
