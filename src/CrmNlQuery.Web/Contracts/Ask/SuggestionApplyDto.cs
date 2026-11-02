namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for applying a follow-up suggestion.
/// </summary>
public sealed record SuggestionApplyDto(string? SuggestionId);
