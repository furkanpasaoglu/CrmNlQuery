namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for saving the display name; empty removes it.
/// </summary>
public sealed record PreferencesDto(string? DisplayName);
