namespace CrmNlQuery.Application.Quality.Models;

/// <summary>
/// Filter for listing acceptance-set cases by status, source or similar criteria.
/// </summary>
public sealed record QualityCaseFilter(string? Status = null, string? Source = null, bool? Verified = null, string? Entity = null);
