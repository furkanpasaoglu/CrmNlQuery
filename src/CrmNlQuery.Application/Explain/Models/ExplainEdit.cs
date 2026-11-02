namespace CrmNlQuery.Application.Explain.Models;

/// <summary>
/// Describes one edit to an explanation chip: which chip, what action and the value.
/// </summary>
public sealed record ExplainEdit(string ChipId, string Action, string? Value);
