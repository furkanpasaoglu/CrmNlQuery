namespace CrmNlQuery.Application.Quality.Models;

/// <summary>
/// Type used by the quality-check feature for generate result.
/// </summary>
public sealed record QualityGenerateResult(int Added, int Duplicates, string? Reason);
