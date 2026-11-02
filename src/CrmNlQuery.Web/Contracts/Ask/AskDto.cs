namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for asking a question, with chat, context and answers.
/// </summary>
public sealed record AskDto(string? Question, Dictionary<string, string>? Answers, string? PreviousResultId = null, RecordContextDto? Context = null, string? ChatId = null, string? CreateDraftId = null);
