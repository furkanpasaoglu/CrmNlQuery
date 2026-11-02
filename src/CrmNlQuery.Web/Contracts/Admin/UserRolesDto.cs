namespace CrmNlQuery.Web.Contracts.Admin;

/// <summary>
/// Request body for replacing a user's roles.
/// </summary>
public sealed record UserRolesDto(List<Guid>? RoleIds);
