namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Holds the model's analysis of a question: primary entity, mentions, related entities and follow-up flags.
/// </summary>
public sealed record QuestionAnalysis(
    string PrimaryEntity,
    IReadOnlyList<string> RelatedEntities,
    IReadOnlyList<Mention> Mentions,
    IReadOnlyList<string> Columns,
    bool Aggregate,
    bool ReferencesPrevious = false,
    bool ReferencesCurrentRecord = false);
