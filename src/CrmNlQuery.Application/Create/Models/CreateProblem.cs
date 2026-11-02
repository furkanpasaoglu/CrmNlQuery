namespace CrmNlQuery.Application.Create.Models;

/// <summary>
/// Describes one validation problem for record creation, with a stable code the client translates.
/// </summary>
public sealed record CreateProblem(string Attribute, string Label, string Code, string Detail = "");
