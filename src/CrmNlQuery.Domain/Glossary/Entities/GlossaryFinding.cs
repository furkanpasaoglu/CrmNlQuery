namespace CrmNlQuery.Domain.Glossary.Entities;

/// <summary>
/// The code-checked result of whether a plan applied a glossary entry, with an error if not.
/// </summary>
public sealed record GlossaryFinding(GlossaryHit Hit, bool Applied, string? Error);
