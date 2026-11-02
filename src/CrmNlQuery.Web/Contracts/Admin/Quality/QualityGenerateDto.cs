namespace CrmNlQuery.Web.Contracts.Admin.Quality;

/// <summary>
/// Request or response data transfer type for quality generate.
/// </summary>
public sealed record QualityGenerateDto(string? Entity, string? Language);
