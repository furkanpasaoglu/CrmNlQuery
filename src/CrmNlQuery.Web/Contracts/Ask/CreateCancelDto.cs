namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for cancelling a pending record creation.
/// </summary>
public sealed record CreateCancelDto(string? ChatId = null);
