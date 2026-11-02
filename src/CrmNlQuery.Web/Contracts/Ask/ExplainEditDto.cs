namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for removing or changing a filter chip in the query explanation.
/// </summary>
public sealed record ExplainEditDto(string? ChipId, string? Action, string? Value);
