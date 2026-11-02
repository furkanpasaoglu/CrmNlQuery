using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Application.Access.Services;

/// <summary>
/// Applies the admin panel's authorization rules, failing closed when the store errors.
/// </summary>
public sealed class AccessService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private readonly IAccessStore _store;
    private readonly IReadOnlyList<string> _bootstrapAccounts;
    private readonly Func<DateTimeOffset> _clock;

    private static readonly TimeSpan DeniedAuditInterval = TimeSpan.FromMinutes(10);
    private const int DeniedAuditMaxKeys = 5000;
    private readonly Dictionary<string, DateTimeOffset> _deniedSeen = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

    private readonly object _cacheGate = new object();
    private readonly Dictionary<string, (DateTimeOffset Expires, AccessPrincipal? Principal)> _cache
        = new Dictionary<string, (DateTimeOffset, AccessPrincipal?)>(StringComparer.Ordinal);

    public AccessService(IAccessStore store, IEnumerable<string> bootstrapAccounts, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _bootstrapAccounts = bootstrapAccounts.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Returns the case-insensitive normalized key for an account name.
    /// </summary>
    public static string Key(string? account) => NameNormalizer.Fold((account ?? "").Trim());

    private static bool LooksLikeAccount(string key)
    {
        var slash = key.IndexOf('\\');
        return key.Length <= 256 && slash > 0 && slash < key.Length - 1 && key.IndexOf('\\', slash + 1) < 0 && !key.Any(char.IsWhiteSpace);
    }

    private static bool LooksLikeGroup(string key)
    {
        var slash = key.IndexOf('\\');
        return key.Length <= 256 && slash > 0 && slash < key.Length - 1 && key.IndexOf('\\', slash + 1) < 0;
    }

    /// <summary>
    /// Adds the configured bootstrap accounts as Administrators when the store has no active Administrator.
    /// </summary>
    public async Task<int> EnsureBootstrapAsync(CancellationToken ct = default)
    {
        if (await CountActiveAdministratorsAsync(ct) > 0)
        {
            return 0;
        }

        var added = 0;
        foreach (var account in _bootstrapAccounts)
        {
            var key = Key(account);
            if (!LooksLikeAccount(key))
            {
                continue;
            }
            await _store.SaveUserAsync(new AccessUser(key, account, true, _clock(), "bootstrap", new[] { AccessRoles.AdministratorId }), ct);
            await AuditAsync("bootstrap", "bootstrap", key, "added as Administrator from configuration", ct);
            added++;
        }
        if (added > 0)
        {
            InvalidateCache();
        }
        return added;
    }

    /// <summary>
    /// Resolves an account to its registered active principal with direct and group-derived roles, or null, throwing if the store fails.
    /// </summary>
    public async Task<AccessPrincipal?> ResolveAsync(string? account, CancellationToken ct = default, Func<string, bool>? isMember = null)
    {
        var key = Key(account);
        if (key.Length == 0)
        {
            return null;
        }

        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var cached) && cached.Expires > _clock())
            {
                return cached.Principal;
            }
        }

        var user = await _store.GetUserAsync(key, ct);
        AccessPrincipal? principal = null;
        if (user is null || user.Active)
        {
            var roleIds = new HashSet<Guid>(user?.RoleIds ?? new List<Guid>());
            if (isMember is not null)
            {
                foreach (var mapping in await _store.ListGroupMappingsAsync(ct))
                {
                    if (isMember(mapping.DisplayName))
                    {
                        roleIds.UnionWith(mapping.RoleIds);
                    }
                }
            }
            var roles = (await _store.ListRolesAsync(ct)).ToDictionary(r => r.Id);
            var assigned = roleIds.Where(roles.ContainsKey).Select(id => roles[id]).ToList();
            if (user is not null || assigned.Count > 0)
            {
                var isAdministrator = assigned.Any(r => r.Builtin);
                var permissions = isAdministrator
                    ? Permissions.All.Select(p => p.Key)
                    : assigned.SelectMany(r => r.Permissions).Where(Permissions.IsKnown);
                principal = new AccessPrincipal(key, user?.DisplayName ?? (account ?? "").Trim(),
                    assigned.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList(), isAdministrator, permissions);
            }
        }

        lock (_cacheGate)
        {
            _cache[key] = (_clock() + CacheLifetime, principal);
        }
        return principal;
    }

    private void InvalidateCache()
    {
        lock (_cacheGate) { _cache.Clear(); }
    }

    /// <summary>
    /// Lists all registered users; the caller must check permission first.
    /// </summary>
    public Task<IReadOnlyList<AccessUser>> ListUsersAsync(CancellationToken ct = default) => _store.ListUsersAsync(ct);

    /// <summary>
    /// Returns all admin roles from the access store.
    /// </summary>
    public Task<IReadOnlyList<AccessRole>> ListRolesAsync(CancellationToken ct = default) => _store.ListRolesAsync(ct);

    /// <summary>
    /// Lists the most recent audit entries up to the given limit.
    /// </summary>
    public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default)
        => _store.ListAuditAsync(Math.Clamp(limit, 1, 500), ct);

    /// <summary>
    /// Checks whether an active Administrator exists.
    /// </summary>
    public async Task<bool> HasAdministratorAsync(CancellationToken ct = default) => await CountActiveAdministratorsAsync(ct) > 0;

    /// <summary>
    /// Adds a user with roles on behalf of an actor, enforcing the role-granting rules.
    /// </summary>
    public async Task<AccessResult> AddUserAsync(AccessPrincipal actor, string account, string? displayName, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        if (!actor.Has(Permissions.UsersManage))
        {
            return Forbidden(Permissions.UsersManage);
        }
        var key = Key(account);
        if (!LooksLikeAccount(key))
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmAccountFormat));
        }
        if (await _store.GetUserAsync(key, ct) is not null)
        {
            return AccessResult.Fail(AccessError.Conflict, MessageCatalog.Now(Msg.AdmUserExists));
        }
        var roles = await LoadRolesAsync(roleIds, ct);
        if (roles is null)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmRoleMissing));
        }
        var denied = FirstUnassignable(actor, roles);
        if (denied is not null)
        {
            return CannotGive(denied);
        }

        var name = string.IsNullOrWhiteSpace(displayName) ? (account ?? "").Trim() : displayName.Trim();
        await _store.SaveUserAsync(new AccessUser(key, name, true, _clock(), actor.Account, roleIds.Distinct().ToList()), ct);
        await AuditAsync(actor.Account, "user.add", key, "roles: " + RoleNames(roles), ct);
        InvalidateCache();
        return AccessResult.Success;
    }

    /// <summary>
    /// Sets a user's roles on behalf of an actor, enforcing the no-escalation rules.
    /// </summary>
    public async Task<AccessResult> SetUserRolesAsync(AccessPrincipal actor, string account, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        if (!actor.Has(Permissions.UsersManage))
        {
            return Forbidden(Permissions.UsersManage);
        }
        var key = Key(account);
        var user = await _store.GetUserAsync(key, ct);
        if (user is null)
        {
            return AccessResult.Fail(AccessError.NotFound, MessageCatalog.Now(Msg.AdmNoSuchUser));
        }
        var wanted = await LoadRolesAsync(roleIds, ct);
        if (wanted is null)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmRoleMissing));
        }

        var all = await _store.ListRolesAsync(ct);
        var changedIds = user.RoleIds.Except(roleIds).Concat(roleIds.Except(user.RoleIds)).Distinct().ToHashSet();
        var denied = FirstUnassignable(actor, all.Where(r => changedIds.Contains(r.Id)).ToList());
        if (denied is not null)
        {
            return CannotGive(denied);
        }

        var wasAdministrator = user.Active && user.RoleIds.Contains(AccessRoles.AdministratorId);
        var willBeAdministrator = roleIds.Contains(AccessRoles.AdministratorId);
        if (wasAdministrator && !willBeAdministrator && await CountActiveAdministratorsAsync(ct) <= 1)
        {
            return LastAdministrator();
        }

        await _store.SaveUserAsync(user with { RoleIds = roleIds.Distinct().ToList() }, ct);
        await AuditAsync(actor.Account, "user.roles", key, "roles: " + RoleNames(wanted), ct);
        InvalidateCache();
        return AccessResult.Success;
    }

    /// <summary>
    /// Activates or deactivates a user on behalf of an actor.
    /// </summary>
    public async Task<AccessResult> SetUserActiveAsync(AccessPrincipal actor, string account, bool active, CancellationToken ct = default)
    {
        if (!actor.Has(Permissions.UsersManage))
        {
            return Forbidden(Permissions.UsersManage);
        }
        var key = Key(account);
        var user = await _store.GetUserAsync(key, ct);
        if (user is null)
        {
            return AccessResult.Fail(AccessError.NotFound, MessageCatalog.Now(Msg.AdmNoSuchUser));
        }
        if (user.Active == active)
        {
            return AccessResult.Success;
        }
        var blocked = active ? await CheckRolesAsync(actor, user, ct) : await CheckRemovalAsync(actor, user, ct);
        if (blocked is not null)
        {
            return blocked;
        }

        await _store.SaveUserAsync(user with { Active = active }, ct);
        await AuditAsync(actor.Account, active ? "user.activate" : "user.deactivate", key, "", ct);
        InvalidateCache();
        return AccessResult.Success;
    }

    /// <summary>
    /// Deletes a user only if they never performed any action, otherwise suggests deactivating to keep the audit trail meaningful.
    /// </summary>
    public async Task<AccessResult> DeleteUserAsync(AccessPrincipal actor, string account, CancellationToken ct = default)
    {
        if (!actor.Has(Permissions.UsersManage))
        {
            return Forbidden(Permissions.UsersManage);
        }
        var key = Key(account);
        var user = await _store.GetUserAsync(key, ct);
        if (user is null)
        {
            return AccessResult.Fail(AccessError.NotFound, MessageCatalog.Now(Msg.AdmNoSuchUser));
        }
        var blocked = await CheckRemovalAsync(actor, user, ct);
        if (blocked is not null)
        {
            return blocked;
        }
        if (await _store.HasAuditByActorAsync(key, ct))
        {
            return AccessResult.Fail(AccessError.Conflict, MessageCatalog.Now(Msg.AdmUserHasAudit));
        }

        await _store.DeleteUserAsync(key, ct);
        await AuditAsync(actor.Account, "user.delete", key, "", ct);
        InvalidateCache();
        return AccessResult.Success;
    }

    private async Task<AccessResult?> CheckRemovalAsync(AccessPrincipal actor, AccessUser user, CancellationToken ct)
    {
        var blocked = await CheckRolesAsync(actor, user, ct);
        if (blocked is not null)
        {
            return blocked;
        }
        if (user.Active && user.RoleIds.Contains(AccessRoles.AdministratorId) && await CountActiveAdministratorsAsync(ct) <= 1)
        {
            return LastAdministrator();
        }
        return null;
    }

    private async Task<AccessResult?> CheckRolesAsync(AccessPrincipal actor, AccessUser user, CancellationToken ct)
    {
        var all = await _store.ListRolesAsync(ct);
        var denied = FirstUnassignable(actor, all.Where(r => user.RoleIds.Contains(r.Id)).ToList());
        return denied is null ? null : CannotGive(denied);
    }

    /// <summary>
    /// Lists the AD group to role mappings.
    /// </summary>
    public Task<IReadOnlyList<GroupMapping>> ListGroupMappingsAsync(CancellationToken ct = default) => _store.ListGroupMappingsAsync(ct);

    /// <summary>
    /// Sets the roles mapped to an AD group (empty list removes it), with the same no-escalation rules as for users and a confirmation for risky grants.
    /// </summary>
    public async Task<AccessResult> SetGroupRolesAsync(AccessPrincipal actor, string group, IReadOnlyList<Guid> roleIds, CancellationToken ct = default, bool confirmed = false)
    {
        if (!actor.Has(Permissions.UsersManage))
        {
            return Forbidden(Permissions.UsersManage);
        }
        var key = Key(group);
        if (!LooksLikeGroup(key))
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmGroupFormat));
        }
        var wanted = await LoadRolesAsync(roleIds, ct);
        if (wanted is null)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmRoleMissing));
        }

        var existing = (await _store.ListGroupMappingsAsync(ct)).FirstOrDefault(m => m.Group == key);
        var before = existing?.RoleIds ?? new List<Guid>();
        var changed = before.Except(roleIds).Concat(roleIds.Except(before)).ToHashSet();
        var denied = FirstUnassignable(actor, (await _store.ListRolesAsync(ct)).Where(r => changed.Contains(r.Id)).ToList());
        if (denied is not null)
        {
            return CannotGive(denied);
        }

        var added = (await _store.ListRolesAsync(ct)).Where(r => roleIds.Contains(r.Id) && !before.Contains(r.Id)).ToList();
        var risk = roleIds.Count == 0 ? GroupMappingRiskKind.None : GroupMappingRisk.Assess(group ?? "", added);
        if (risk != GroupMappingRiskKind.None && !confirmed)
        {
            return AccessResult.Fail(AccessError.ConfirmationRequired, GroupMappingRisk.Message(risk, (group ?? "").Trim()));
        }

        if (roleIds.Count == 0)
        {
            if (existing is not null)
            {
                await _store.DeleteGroupMappingAsync(key, ct);
                await AuditAsync(actor.Account, "group.unmap", key, "", ct);
                InvalidateCache();
            }
            return AccessResult.Success;
        }

        await _store.SaveGroupMappingAsync(new GroupMapping(key, (group ?? "").Trim(), roleIds.Distinct().ToList()), ct);
        await AuditAsync(actor.Account, "group.roles", key, "roles: " + RoleNames(wanted) + (risk != GroupMappingRiskKind.None ? "; " + GroupMappingRisk.AuditNote(risk) : ""), ct);
        InvalidateCache();
        return AccessResult.Success;
    }

    /// <summary>
    /// Creates a role with the given permissions on behalf of an actor.
    /// </summary>
    public async Task<AccessResult> CreateRoleAsync(AccessPrincipal actor, string name, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        if (!actor.Has(Permissions.RolesManage))
        {
            return Forbidden(Permissions.RolesManage);
        }
        var invalid = await ValidateRoleAsync(name, null, permissions, ct);
        if (invalid is not null)
        {
            return invalid;
        }
        var missing = permissions.FirstOrDefault(p => !actor.Has(p));
        if (missing is not null)
        {
            return AccessResult.Fail(AccessError.Forbidden, MessageCatalog.Now(Msg.AdmCannotGivePermission, MessageCatalog.Args(("missing", missing))));
        }

        var role = new AccessRole(Guid.NewGuid(), name.Trim(), false, permissions.Distinct().ToList());
        await _store.SaveRoleAsync(role, ct);
        await AuditAsync(actor.Account, "role.create", role.Name, "permissions: " + string.Join(", ", role.Permissions), ct);
        return AccessResult.Created(role.Id);
    }

    /// <summary>
    /// Updates a role's name and permissions on behalf of an actor.
    /// </summary>
    public async Task<AccessResult> UpdateRoleAsync(AccessPrincipal actor, Guid id, string name, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        if (!actor.Has(Permissions.RolesManage))
        {
            return Forbidden(Permissions.RolesManage);
        }
        var role = await _store.GetRoleAsync(id, ct);
        if (role is null)
        {
            return AccessResult.Fail(AccessError.NotFound, MessageCatalog.Now(Msg.AdmNoSuchRole));
        }
        if (role.Builtin)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmBuiltinNoChange));
        }
        var invalid = await ValidateRoleAsync(name, id, permissions, ct);
        if (invalid is not null)
        {
            return invalid;
        }
        var missing = permissions.Except(role.Permissions).FirstOrDefault(p => !actor.Has(p));
        if (missing is not null)
        {
            return AccessResult.Fail(AccessError.Forbidden, MessageCatalog.Now(Msg.AdmCannotGivePermission, MessageCatalog.Args(("missing", missing))));
        }

        var updated = role with { Name = name.Trim(), Permissions = permissions.Distinct().ToList() };
        await _store.SaveRoleAsync(updated, ct);
        await AuditAsync(actor.Account, "role.update", updated.Name, "permissions: " + string.Join(", ", updated.Permissions), ct);
        InvalidateCache();
        return AccessResult.Success;
    }

    /// <summary>
    /// Deletes a role on behalf of an actor.
    /// </summary>
    public async Task<AccessResult> DeleteRoleAsync(AccessPrincipal actor, Guid id, CancellationToken ct = default)
    {
        if (!actor.Has(Permissions.RolesManage))
        {
            return Forbidden(Permissions.RolesManage);
        }
        var role = await _store.GetRoleAsync(id, ct);
        if (role is null)
        {
            return AccessResult.Fail(AccessError.NotFound, MessageCatalog.Now(Msg.AdmNoSuchRole));
        }
        if (role.Builtin)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmBuiltinNoDelete));
        }

        await _store.DeleteRoleAsync(id, ct);
        await AuditAsync(actor.Account, "role.delete", role.Name, "", ct);
        InvalidateCache();
        return AccessResult.Success;
    }

    private async Task<AccessResult?> ValidateRoleAsync(string name, Guid? selfId, IReadOnlyList<string> permissions, CancellationToken ct)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > 64)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmRoleNameLength));
        }
        var unknown = permissions.FirstOrDefault(p => !Permissions.IsKnown(p));
        if (unknown is not null)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmUnknownPermission, MessageCatalog.Args(("unknown", unknown))));
        }
        var folded = NameNormalizer.Fold(trimmed);
        var taken = (await _store.ListRolesAsync(ct)).Any(r => r.Id != selfId && NameNormalizer.Fold(r.Name) == folded);
        if (taken)
        {
            return AccessResult.Fail(AccessError.Conflict, MessageCatalog.Now(Msg.AdmRoleExists));
        }
        return null;
    }

    private async Task<IReadOnlyList<AccessRole>?> LoadRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        var all = (await _store.ListRolesAsync(ct)).ToDictionary(r => r.Id);
        var found = new List<AccessRole>();
        foreach (var id in roleIds.Distinct())
        {
            if (!all.TryGetValue(id, out var role))
            {
                return null;
            }
            found.Add(role);
        }
        return found;
    }

    private static AccessRole? FirstUnassignable(AccessPrincipal actor, IEnumerable<AccessRole> roles)
    {
        foreach (var role in roles)
        {
            var allowed = role.Builtin ? actor.IsAdministrator : role.Permissions.All(actor.Has);
            if (!allowed)
            {
                return role;
            }
        }
        return null;
    }

    private async Task<int> CountActiveAdministratorsAsync(CancellationToken ct)
        => (await _store.ListUsersAsync(ct)).Count(u => u.Active && u.RoleIds.Contains(AccessRoles.AdministratorId));

    /// <summary>
    /// Records an audit entry when a known user's admin action is denied, without values or query strings.
    /// </summary>
    public Task RecordDeniedAsync(string account, string endpoint, string? permission, string reason, CancellationToken ct = default)
        => RecordAccessAsync("access.denied", account, endpoint, permission, reason, null, ct);

    /// <summary>
    /// Records an access.denied or access.would_deny audit entry, including the user's AD group names only when a record is actually written.
    /// </summary>
    public async Task RecordAccessAsync(string action, string account, string endpoint, string? permission, string reason, Func<string?>? groups, CancellationToken ct = default)
    {
        var actor = Key(account);
        var key = action + "|" + actor + "|" + endpoint + "|" + reason;
        if (!ShouldWrite(key))
        {
            return;
        }
        var detail = $"needs {permission ?? "registration"}; {reason}";
        string? names = null;
        try { names = groups?.Invoke(); }
        catch (Exception) { names = null; }
        if (!string.IsNullOrWhiteSpace(names))
        {
            var trimmed = names.Trim();
            detail += "; groups: " + (trimmed.Length > 800 ? trimmed.Substring(0, 800) + "..." : trimmed);
        }
        await AuditAsync(actor, action, endpoint, detail, ct);
    }

    private bool ShouldWrite(string key)
    {
        lock (_deniedSeen)
        {
            var now = _clock();
            if (_deniedSeen.TryGetValue(key, out var last) && now - last < DeniedAuditInterval)
            {
                return false;
            }
            if (_deniedSeen.Count >= DeniedAuditMaxKeys)
            {
                foreach (var expired in _deniedSeen.Where(p => now - p.Value >= DeniedAuditInterval).Select(p => p.Key).ToList())
                {
                    _deniedSeen.Remove(expired);
                }
                if (_deniedSeen.Count >= DeniedAuditMaxKeys)
                {
                    return false;
                }
            }
            _deniedSeen[key] = now;
            return true;
        }
    }

    /// <summary>
    /// Records the viewing of a sensitive record (who, what, which filter) without its content, with optional rate limiting.
    /// </summary>
    public async Task RecordViewAsync(string action, string account, string target, string dedupeKey, string detail, bool rateLimit, CancellationToken ct = default)
    {
        var actor = Key(account);
        if (rateLimit && !ShouldWrite(action + "|" + actor + "|" + target + "|" + dedupeKey))
        {
            return;
        }
        await AuditAsync(actor, action, target, detail, ct);
    }

    /// <summary>
    /// Records an audit entry for tracked non-admin operations, throwing on failure so the caller can swallow it.
    /// </summary>
    public Task RecordAsync(string actor, string action, string target, string detail, CancellationToken ct = default)
        => AuditAsync(actor, action, target, detail, ct);

    private Task AuditAsync(string actor, string action, string target, string detail, CancellationToken ct)
        => _store.AddAuditAsync(new AuditEntry(_clock(), actor, action, target, detail), ct);

    private static string RoleNames(IEnumerable<AccessRole> roles)
        => string.Join(", ", roles.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal));

    private static AccessResult Forbidden(string permission)
        => AccessResult.Fail(AccessError.Forbidden, MessageCatalog.Now(Msg.AdmNeedsPermission, MessageCatalog.Args(("permission", permission))));

    private static AccessResult CannotGive(AccessRole role)
        => AccessResult.Fail(AccessError.Forbidden, MessageCatalog.Now(Msg.AdmCannotGiveRole, MessageCatalog.Args(("role", role.Name))));

    private static AccessResult LastAdministrator()
        => AccessResult.Fail(AccessError.Conflict, MessageCatalog.Now(Msg.AdmLastAdmin));
}
