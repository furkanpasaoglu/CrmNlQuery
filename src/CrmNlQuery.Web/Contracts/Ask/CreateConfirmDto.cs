namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for confirming record creation, with the duplicate choice and chat id.
/// </summary>
public sealed record CreateConfirmDto(bool? AllowDuplicates = null, string? ChatId = null);
