namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Describes a chain step's dependency on an earlier step's ids and their possible entities.
/// </summary>
public sealed record StepPlanInfo(int? UsesStep, IReadOnlyList<string> IdEntities, bool ProducesIds);
