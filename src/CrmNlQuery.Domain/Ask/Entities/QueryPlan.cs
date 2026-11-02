namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Represents a validated query plan made of ordered steps.
/// </summary>
public sealed record QueryPlan(IReadOnlyList<PlanStep> Steps);
