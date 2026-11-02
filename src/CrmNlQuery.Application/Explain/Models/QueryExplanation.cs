namespace CrmNlQuery.Application.Explain.Models;

/// <summary>
/// Holds the structured, model-free explanation of an executed query: entity, filters, sorts, measures and links.
/// </summary>
public sealed record QueryExplanation(string Entity, string EntityLabel, IReadOnlyList<ExplainFilter> Filters, IReadOnlyList<ExplainSort> Sort,
    int? Top, bool Aggregate, IReadOnlyList<string> GroupBy)
{
    public IReadOnlyList<ExplainMeasure> Measures { get; init; } = Array.Empty<ExplainMeasure>();
}
