namespace CrmNlQuery.Application.Quality.Models;

/// <summary>
/// Type used by the quality-check feature for save result.
/// </summary>
public sealed record QualitySaveResult(QualitySaveStatus Status, string? Reason = null, string? Id = null, int Rows = 0);
