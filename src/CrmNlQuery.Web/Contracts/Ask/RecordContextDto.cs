namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body naming the CRM record shown on the form.
/// </summary>
public sealed record RecordContextDto(string? Entity, string? Id);
