namespace CrmNlQuery.Web.Contracts.Admin;

/// <summary>
/// Request body for adding a user with account, display name and roles.
/// </summary>
public sealed record NewUserDto(string? Account, string? DisplayName, List<Guid>? RoleIds);
