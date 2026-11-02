namespace CrmNlQuery.Application.Explain.Models;

/// <summary>
/// Represents a filter chip in a query explanation with a stable id, field, operator and values.
/// </summary>
public sealed record ExplainFilter(string Id, string Field, string FieldLabel, string Op, IReadOnlyList<ExplainValue> Values)
{
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Via { get; init; }
    public string? Group { get; init; }
    public FilterEdit? Edit { get; init; }
}
