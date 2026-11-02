namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for counting a suggestion click by kind.
/// </summary>
public sealed record SuggestionClickDto(string? Kind);
