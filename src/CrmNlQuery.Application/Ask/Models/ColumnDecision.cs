namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Holds the column decision, either the columns to use or a question to ask the user.
/// </summary>
public sealed record ColumnDecision(IReadOnlyList<string>? Columns, QuestionOutcome? Question);
