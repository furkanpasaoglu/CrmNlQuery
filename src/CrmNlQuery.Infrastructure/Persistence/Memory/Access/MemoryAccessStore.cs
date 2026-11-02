using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// Stores users, roles, group mappings and audit entries in memory when no database is configured, and in tests.
/// </summary>
public sealed class MemoryAccessStore : IAccessStore, CrmNlQuery.Application.Export.Abstractions.IAuditPager
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, AccessUser> _users = new Dictionary<string, AccessUser>(StringComparer.Ordinal);
    private readonly Dictionary<Guid, AccessRole> _roles = new Dictionary<Guid, AccessRole>();
    private readonly Dictionary<string, GroupMapping> _groups = new Dictionary<string, GroupMapping>(StringComparer.Ordinal);
    private readonly List<AuditEntry> _audit = new List<AuditEntry>();

    public MemoryAccessStore()
    {
        _roles[AccessRoles.AdministratorId] = new AccessRole(AccessRoles.AdministratorId, AccessRoles.AdministratorName, true, new List<string>());
    }

    /// <summary>
    /// Returns all in-memory admin users ordered by account name, read under the store lock.
    /// </summary>
    public Task<IReadOnlyList<AccessUser>> ListUsersAsync(CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<AccessUser>>(_users.Values.OrderBy(u => u.Account, StringComparer.Ordinal).ToList()); }
    }

    /// <summary>
    /// Returns the in-memory admin user with the given account, or null when it does not exist.
    /// </summary>
    public Task<AccessUser?> GetUserAsync(string account, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_users.TryGetValue(account, out var user) ? user : null); }
    }

    /// <summary>
    /// Stores or replaces an admin user by account in memory, removing duplicate role ids.
    /// </summary>
    public Task SaveUserAsync(AccessUser user, CancellationToken ct = default)
    {
        lock (_gate) { _users[user.Account] = user with { RoleIds = user.RoleIds.Distinct().ToList() }; }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the admin user with the given account from the in-memory store, doing nothing if absent.
    /// </summary>
    public Task DeleteUserAsync(string account, CancellationToken ct = default)
    {
        lock (_gate) { _users.Remove(account); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns all in-memory admin roles ordered by name, read under the store lock.
    /// </summary>
    public Task<IReadOnlyList<AccessRole>> ListRolesAsync(CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<AccessRole>>(_roles.Values.OrderBy(r => r.Name, StringComparer.Ordinal).ToList()); }
    }

    /// <summary>
    /// Returns the in-memory admin role with the given id, or null when it does not exist.
    /// </summary>
    public Task<AccessRole?> GetRoleAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_roles.TryGetValue(id, out var role) ? role : null); }
    }

    /// <summary>
    /// Stores or replaces an admin role by id in memory, removing duplicate permissions.
    /// </summary>
    public Task SaveRoleAsync(AccessRole role, CancellationToken ct = default)
    {
        lock (_gate) { _roles[role.Id] = role with { Permissions = role.Permissions.Distinct().ToList() }; }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes an admin role from memory and also strips that role from any AD group mappings that referenced it.
    /// </summary>
    public Task DeleteRoleAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _roles.Remove(id);
            foreach (var mapping in _groups.Values.ToList())
            {
                if (mapping.RoleIds.Contains(id))
                {
                    _groups[mapping.Group] = mapping with { RoleIds = mapping.RoleIds.Where(r => r != id).ToList() };
                }
            }
            foreach (var user in _users.Values.ToList())
            {
                if (user.RoleIds.Contains(id))
                {
                    _users[user.Account] = user with { RoleIds = user.RoleIds.Where(r => r != id).ToList() };
                }
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns all in-memory AD group-to-role mappings ordered by group name, read under the store lock.
    /// </summary>
    public Task<IReadOnlyList<GroupMapping>> ListGroupMappingsAsync(CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<GroupMapping>>(_groups.Values.OrderBy(g => g.Group, StringComparer.Ordinal).ToList()); }
    }

    /// <summary>
    /// Stores or replaces an AD group mapping by group name in memory, removing duplicate role ids.
    /// </summary>
    public Task SaveGroupMappingAsync(GroupMapping mapping, CancellationToken ct = default)
    {
        lock (_gate) { _groups[mapping.Group] = mapping with { RoleIds = mapping.RoleIds.Distinct().ToList() }; }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the AD group mapping for the given group from the in-memory store, doing nothing if absent.
    /// </summary>
    public Task DeleteGroupMappingAsync(string group, CancellationToken ct = default)
    {
        lock (_gate) { _groups.Remove(group); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Appends an administration audit entry to the in-memory audit list under the store lock.
    /// </summary>
    public Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default)
    {
        lock (_gate) { _audit.Add(entry); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns one page of audit entries for export, oldest first, using the insertion order as cursor.
    /// </summary>
    public Task<IReadOnlyList<CrmNlQuery.Application.Export.Models.AuditPageItem>> PageAuditAsync(DateTimeOffset from, DateTimeOffset to, string? actor, string? actionPrefix,
        CrmNlQuery.Application.Export.Models.AuditCursor? after, int size, CancellationToken ct = default)
    {
        var prefix = (actionPrefix ?? "").Trim().ToLowerInvariant();
        lock (_gate)
        {
            var page = _audit.Select((entry, index) => new CrmNlQuery.Application.Export.Models.AuditPageItem(index + 1, entry))
                .Where(x => x.Entry.At >= from && x.Entry.At < to)
                .Where(x => string.IsNullOrWhiteSpace(actor) || string.Equals(x.Entry.Actor, actor.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(x => prefix.Length == 0 || x.Entry.Action.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Entry.At).ThenBy(x => x.Id)
                .Where(x => after is null || x.Entry.At > after.At || (x.Entry.At == after.At && x.Id > after.Id))
                .Take(Math.Max(1, size)).ToList();
            return Task.FromResult<IReadOnlyList<CrmNlQuery.Application.Export.Models.AuditPageItem>>(page);
        }
    }

    /// <summary>
    /// Returns up to the given number of in-memory audit entries newest first, breaking time ties by insertion order so later additions come first.
    /// </summary>
    public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var newestFirst = _audit.Select((entry, index) => (entry, index))
                .OrderByDescending(x => x.entry.At).ThenByDescending(x => x.index)
                .Select(x => x.entry).Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<AuditEntry>>(newestFirst);
        }
    }

    /// <summary>
    /// Checks whether there is audit by actor in memory.
    /// </summary>
    public Task<bool> HasAuditByActorAsync(string account, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(_audit.Any(a => a.Actor == account)); }
    }
}
