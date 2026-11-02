namespace CrmNlQuery.Web.Contracts.Admin.Quality;

/// <summary>
/// Request or response data transfer type for quality ids.
/// </summary>
public sealed record QualityIdsDto(List<string>? Ids);
