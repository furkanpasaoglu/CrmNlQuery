namespace CrmNlQuery.Application.Explain.Models;

/// <summary>
/// Represents an aggregate measure in a query explanation: function, field label and alias.
/// </summary>
public sealed record ExplainMeasure(string Fn, string? FieldLabel, string Alias);
