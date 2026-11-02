using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Carries everything the plan generator needs: question, entities, resolved records, columns and context.
/// </summary>
public sealed record PlanRequest(
    string Question,
    IReadOnlyList<EntityInfo> Entities,
    IReadOnlyList<ResolvedRecord> Resolved,
    IReadOnlyList<string> Columns,
    bool Aggregate,
    IReadOnlyList<string>? Knowledge = null,
    PreviousQuery? Previous = null,
    StepPlanInfo? Step = null,
    IReadOnlyList<CrmNlQuery.Domain.Glossary.Entities.GlossaryHit>? Glossary = null);
