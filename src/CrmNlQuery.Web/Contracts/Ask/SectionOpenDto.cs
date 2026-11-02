namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for opening a summary section in CRM.
/// </summary>
public sealed record SectionOpenDto(string? Kind, string? Entity, string? ViaEntity);
