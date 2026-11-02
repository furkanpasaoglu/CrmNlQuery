namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Carries the result of plan generation: success, FetchXML, or a flag that the question needs several steps.
/// </summary>
public sealed record PlanOutcome(bool Success, string? FetchXml, IReadOnlyList<string> Errors, bool NeedsSteps = false, bool CannotAnswer = false)
{
    public IReadOnlyList<CrmNlQuery.Domain.Glossary.Entities.GlossaryFinding> Glossary { get; init; } = Array.Empty<CrmNlQuery.Domain.Glossary.Entities.GlossaryFinding>();
}
