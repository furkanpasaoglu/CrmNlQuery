namespace CrmNlQuery.Web.Contracts.Admin.Quality;

/// <summary>
/// Request or response data transfer type for quality schedule.
/// </summary>
public sealed record QualityScheduleDto(bool? Enabled, int? Hour, int? Repeat);
