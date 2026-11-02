namespace CrmNlQuery.Application.Quality.Models;

/// <summary>
/// Represents one generated starter acceptance-set case with its entity, template and question.
/// </summary>
public sealed record StarterCase(string Id, string Entity, string Template, string Question, string FetchXml, string Columns, string Ties, string? AnswerColumns, string Note);
