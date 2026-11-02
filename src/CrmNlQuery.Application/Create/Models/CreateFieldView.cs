namespace CrmNlQuery.Application.Create.Models;

/// <summary>
/// Describes one field of a creation preview as a label and value.
/// </summary>
public sealed record CreateFieldView(string Label, string Value);
