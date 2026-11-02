namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Result type returned by the ask pipeline for question outcomes.
/// </summary>
public sealed record QuestionOutcome(string Kind, string AnswerKey, string Text, IReadOnlyList<Choice> Choices, bool AllowFreeText) : AskOutcome;
