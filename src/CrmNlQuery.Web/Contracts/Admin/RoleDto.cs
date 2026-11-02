namespace CrmNlQuery.Web.Contracts.Admin;

/// <summary>
/// Request body for creating or updating a role with its permissions.
/// </summary>
public sealed record RoleDto(string? Name, List<string>? Permissions);
