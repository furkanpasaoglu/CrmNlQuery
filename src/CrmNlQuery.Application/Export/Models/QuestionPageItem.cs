namespace CrmNlQuery.Application.Export.Models;

/// <summary>
/// One question text entry returned by a page of the question pager.
/// </summary>
public sealed record QuestionPageItem(string Id, Guid Owner, DateTimeOffset At, string Question, string Entity);
