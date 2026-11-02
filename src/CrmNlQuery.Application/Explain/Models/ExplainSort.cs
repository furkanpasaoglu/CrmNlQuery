namespace CrmNlQuery.Application.Explain.Models;

/// <summary>
/// Represents a sort in a query explanation.
/// </summary>
public sealed record ExplainSort(string Field, string Label, bool Descending)
{
    public string? Fn { get; init; }
}
