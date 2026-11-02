using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.Access.Abstractions;

/// <summary>
/// Persistence contract for users, roles, AD group mappings and audit entries; business rules live in AccessService.
/// </summary>
public interface IAccessStore
{
    /// <summary>
    /// Storage contract that returns all admin users from the access store.
    /// </summary>
    Task<IReadOnlyList<AccessUser>> ListUsersAsync(CancellationToken ct = default);
    /// <summary>
    /// Storage contract that returns the admin user with the given account, or null when none exists.
    /// </summary>
    Task<AccessUser?> GetUserAsync(string account, CancellationToken ct = default);
    /// <summary>
    /// Adds or updates a user, replacing their roles entirely.
    /// </summary>
    Task SaveUserAsync(AccessUser user, CancellationToken ct = default);
    /// <summary>
    /// Storage contract that removes the admin user with the given account from the access store.
    /// </summary>
    Task DeleteUserAsync(string account, CancellationToken ct = default);

    /// <summary>
    /// Storage contract that returns all admin roles from the access store.
    /// </summary>
    Task<IReadOnlyList<AccessRole>> ListRolesAsync(CancellationToken ct = default);
    /// <summary>
    /// Storage contract that returns the admin role with the given id, or null when none exists.
    /// </summary>
    Task<AccessRole?> GetRoleAsync(Guid id, CancellationToken ct = default);
    /// <summary>
    /// Storage contract that creates or replaces an admin role together with its permissions.
    /// </summary>
    Task SaveRoleAsync(AccessRole role, CancellationToken ct = default);
    /// <summary>
    /// Deletes a role and its assignments on users.
    /// </summary>
    Task DeleteRoleAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lists AD group mappings; saving replaces a group's roles and deleting a role removes it from mappings.
    /// </summary>
    Task<IReadOnlyList<GroupMapping>> ListGroupMappingsAsync(CancellationToken ct = default);
    /// <summary>
    /// Storage contract that creates or replaces the role mapping of an AD group.
    /// </summary>
    Task SaveGroupMappingAsync(GroupMapping mapping, CancellationToken ct = default);
    /// <summary>
    /// Storage contract that removes the role mapping of the given AD group.
    /// </summary>
    Task DeleteGroupMappingAsync(string group, CancellationToken ct = default);

    /// <summary>
    /// Storage contract that appends one administration audit entry, which never contains question text.
    /// </summary>
    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);
    /// <summary>
    /// Storage contract that returns up to the given number of audit entries, newest first.
    /// </summary>
    Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default);
    /// <summary>
    /// Checks whether there is audit by actor (storage contract).
    /// </summary>
    Task<bool> HasAuditByActorAsync(string account, CancellationToken ct = default);
}
