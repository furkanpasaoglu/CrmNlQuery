namespace CrmNlQuery.Application.Quality.Models;

/// <summary>
/// Type used by the quality-check feature for save rule.
/// </summary>
public sealed record QualitySaveRule(string Order, string Columns);
