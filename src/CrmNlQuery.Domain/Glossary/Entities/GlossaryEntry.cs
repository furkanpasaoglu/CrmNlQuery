using CrmNlQuery.Domain.Glossary.Constants;

namespace CrmNlQuery.Domain.Glossary.Entities;

/// <summary>
/// A business glossary entry mapping company terms to conditions or a table.
/// </summary>
public sealed record GlossaryEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Kind { get; init; } = GlossaryKind.Filter;
    public IReadOnlyList<GlossaryTerm> Terms { get; init; } = new List<GlossaryTerm>();
    public string Entity { get; init; } = "";
    public string? Attribute { get; init; }
    public IReadOnlyList<GlossaryCondition> Conditions { get; init; } = new List<GlossaryCondition>();
    public string Description { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public string ModifiedBy { get; init; } = "";
    public DateTimeOffset ModifiedAt { get; init; }
}
