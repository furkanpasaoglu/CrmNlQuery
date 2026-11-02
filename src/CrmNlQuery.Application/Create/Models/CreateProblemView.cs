namespace CrmNlQuery.Application.Create.Models;

/// <summary>
/// Describes one problem with a creation row: a code the client translates, a label and detail.
/// </summary>
public sealed record CreateProblemView(string Code, string Label, string Detail);
