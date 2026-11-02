using CrmNlQuery.Domain.History.Entities;

namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Result of a question-view request: its status and the viewable questions returned.
/// </summary>
public sealed record QuestionViewResult(QuestionViewStatus Status, IReadOnlyList<ViewableQuestion> Questions);
