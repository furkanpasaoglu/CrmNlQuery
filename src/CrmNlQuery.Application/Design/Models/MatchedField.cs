namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Schema-search evidence for the design assistant: a field of an existing table, with its label, type and matched term.
/// </summary>
public sealed record MatchedField(string Logical, string Label, string Type, string Term);
