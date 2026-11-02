using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Port that turns a user question into a structured analysis of entities and mentions, with optional knowledge, previous query, open record and glossary context.
/// </summary>
public interface IQuestionAnalyzer
{
    /// <summary>
    /// Analyzes a question against the list of entities.
    /// </summary>
    Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default);

    /// <summary>
    /// Analyzes a question with company knowledge; the default implementation ignores the knowledge.
    /// </summary>
    Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, CancellationToken ct = default)
        => AnalyzeAsync(question, entities, ct);

    /// <summary>
    /// Analyzes a question with the previous answer's query for follow-ups; null previous behaves like the base overload.
    /// </summary>
    Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, PreviousQuery? previous, CancellationToken ct = default)
        => AnalyzeAsync(question, entities, knowledge, ct);

    /// <summary>
    /// Analyzes a question with the record open on the user's form; null current behaves like the base overload.
    /// </summary>
    Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, PreviousQuery? previous, ResolvedRecord? current, CancellationToken ct = default)
        => AnalyzeAsync(question, entities, knowledge, previous, ct);

    /// <summary>
    /// Analyzes a question with matched business glossary entries as table-level hints.
    /// </summary>
    Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, PreviousQuery? previous, ResolvedRecord? current,
        IReadOnlyList<CrmNlQuery.Domain.Glossary.Entities.GlossaryHit> glossary, CancellationToken ct = default)
        => AnalyzeAsync(question, entities, knowledge, previous, current, ct);
}
