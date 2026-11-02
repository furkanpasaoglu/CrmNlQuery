namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Represents one plan step: its FetchXML and an optional run-only-if condition.
/// </summary>
public sealed record PlanStep(string FetchXml, string? RunOnlyIf = null);
