namespace CrmNlQuery.Domain.Access.Entities;

/// <summary>
/// Describes one permission with its name and group.
/// </summary>
public sealed record PermissionInfo(string Key, string Group, string Description);
