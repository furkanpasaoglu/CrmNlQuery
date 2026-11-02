namespace CrmNlQuery.Application.Explain.Models;

/// <summary>
/// Describes how a filter chip can be edited: its kind, options and bounds.
/// </summary>
public sealed record FilterEdit(string Kind, IReadOnlyList<ExplainValue>? Options);
