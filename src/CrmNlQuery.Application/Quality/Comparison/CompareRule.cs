namespace CrmNlQuery.Application.Quality.Comparison;

/// <summary>
/// Holds the per-case comparison rules with their defaults.
/// </summary>
public sealed class CompareRule
{
    public string Order { get; set; } = "auto";
    public string Duplicates { get; set; } = "multiset";
    public string Columns { get; set; } = "reference";
    public double? Tolerance { get; set; }
    public string Ties { get; set; } = "strict";
}
