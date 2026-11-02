namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for saving a widget result to the acceptance set.
/// </summary>
public sealed record QualitySaveDto(string? Order, string? Columns, bool? Confirmed);
