using Npgsql;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Stores users, roles, group mappings and audit entries in PostgreSQL using parameterized SQL and UTC timestamps.
/// </summary>
public sealed class PostgresAccessStore : IAccessStore, CrmNlQuery.Application.Export.Abstractions.IAuditPager
{
    private readonly NpgsqlDataSource _db;

    private PostgresAccessStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Applies schema migrations and returns a store backed by a pooled connection.
    /// </summary>
    public static async Task<PostgresAccessStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresAccessStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Reads all admin users with their assigned role ids from PostgreSQL, ordered by account.
    /// </summary>
    public async Task<IReadOnlyList<AccessUser>> ListUsersAsync(CancellationToken ct = default)
    {
        var users = new List<(string Account, string Name, bool Active, DateTimeOffset At, string By)>();
        await using (var command = _db.CreateCommand("select account, display_name, active, created_at, created_by from admin_users order by account"))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                users.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4)));
            }
        }

        var roles = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        await using (var command = _db.CreateCommand("select account, role_id from admin_user_roles"))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var account = reader.GetString(0);
                if (!roles.TryGetValue(account, out var list))
                {
                    roles[account] = list = new List<Guid>();
                }
                list.Add(reader.GetGuid(1));
            }
        }

        return users.Select(u => new AccessUser(u.Account, u.Name, u.Active, u.At, u.By,
            roles.TryGetValue(u.Account, out var r) ? r : new List<Guid>())).ToList();
    }

    /// <summary>
    /// Returns the user with the given account, or null.
    /// </summary>
    public async Task<AccessUser?> GetUserAsync(string account, CancellationToken ct = default)
    {
        AccessUser? user = null;
        await using (var command = _db.CreateCommand("select display_name, active, created_at, created_by from admin_users where account = @account"))
        {
            command.Parameters.AddWithValue("account", account);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                user = new AccessUser(account, reader.GetString(0), reader.GetBoolean(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3), new List<Guid>());
            }
        }
        if (user is null)
        {
            return null;
        }

        var roles = new List<Guid>();
        await using (var command = _db.CreateCommand("select role_id from admin_user_roles where account = @account"))
        {
            command.Parameters.AddWithValue("account", account);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                roles.Add(reader.GetGuid(0));
            }
        }
        return user with { RoleIds = roles };
    }

    /// <summary>
    /// Upserts an admin user and rewrites its role assignments in one PostgreSQL transaction.
    /// </summary>
    public async Task SaveUserAsync(AccessUser user, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var upsert = new NpgsqlCommand(
            "insert into admin_users (account, display_name, active, created_at, created_by) values (@account, @name, @active, @at, @by) " +
            "on conflict (account) do update set display_name = excluded.display_name, active = excluded.active", connection, transaction))
        {
            upsert.Parameters.AddWithValue("account", user.Account);
            upsert.Parameters.AddWithValue("name", user.DisplayName);
            upsert.Parameters.AddWithValue("active", user.Active);
            upsert.Parameters.AddWithValue("at", user.CreatedAt.ToUniversalTime());
            upsert.Parameters.AddWithValue("by", user.CreatedBy);
            await upsert.ExecuteNonQueryAsync(ct);
        }
        await using (var clear = new NpgsqlCommand("delete from admin_user_roles where account = @account", connection, transaction))
        {
            clear.Parameters.AddWithValue("account", user.Account);
            await clear.ExecuteNonQueryAsync(ct);
        }
        foreach (var roleId in user.RoleIds.Distinct())
        {
            await using var add = new NpgsqlCommand("insert into admin_user_roles (account, role_id) values (@account, @role)", connection, transaction);
            add.Parameters.AddWithValue("account", user.Account);
            add.Parameters.AddWithValue("role", roleId);
            await add.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Deletes the user with the given account.
    /// </summary>
    public async Task DeleteUserAsync(string account, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from admin_users where account = @account");
        command.Parameters.AddWithValue("account", account);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Reads all admin roles with their permissions from PostgreSQL, ordered by name.
    /// </summary>
    public async Task<IReadOnlyList<AccessRole>> ListRolesAsync(CancellationToken ct = default)
    {
        var roles = new List<(Guid Id, string Name, bool Builtin)>();
        await using (var command = _db.CreateCommand("select id, name, builtin from admin_roles order by name"))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                roles.Add((reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2)));
            }
        }

        var permissions = new Dictionary<Guid, List<string>>();
        await using (var command = _db.CreateCommand("select role_id, permission from admin_role_permissions"))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                if (!permissions.TryGetValue(id, out var list))
                {
                    permissions[id] = list = new List<string>();
                }
                list.Add(reader.GetString(1));
            }
        }

        return roles.Select(r => new AccessRole(r.Id, r.Name, r.Builtin,
            permissions.TryGetValue(r.Id, out var p) ? p : new List<string>())).ToList();
    }

    /// <summary>
    /// Returns the role with the given id, or null.
    /// </summary>
    public async Task<AccessRole?> GetRoleAsync(Guid id, CancellationToken ct = default)
    {
        AccessRole? role = null;
        await using (var command = _db.CreateCommand("select name, builtin from admin_roles where id = @id"))
        {
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                role = new AccessRole(id, reader.GetString(0), reader.GetBoolean(1), new List<string>());
            }
        }
        if (role is null)
        {
            return null;
        }

        var permissions = new List<string>();
        await using (var command = _db.CreateCommand("select permission from admin_role_permissions where role_id = @id"))
        {
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                permissions.Add(reader.GetString(0));
            }
        }
        return role with { Permissions = permissions };
    }

    /// <summary>
    /// Upserts an admin role and rewrites its permission list in one PostgreSQL transaction.
    /// </summary>
    public async Task SaveRoleAsync(AccessRole role, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var upsert = new NpgsqlCommand(
            "insert into admin_roles (id, name, builtin) values (@id, @name, @builtin) " +
            "on conflict (id) do update set name = excluded.name", connection, transaction))
        {
            upsert.Parameters.AddWithValue("id", role.Id);
            upsert.Parameters.AddWithValue("name", role.Name);
            upsert.Parameters.AddWithValue("builtin", role.Builtin);
            await upsert.ExecuteNonQueryAsync(ct);
        }
        await using (var clear = new NpgsqlCommand("delete from admin_role_permissions where role_id = @id", connection, transaction))
        {
            clear.Parameters.AddWithValue("id", role.Id);
            await clear.ExecuteNonQueryAsync(ct);
        }
        foreach (var permission in role.Permissions.Distinct())
        {
            await using var add = new NpgsqlCommand("insert into admin_role_permissions (role_id, permission) values (@id, @permission)", connection, transaction);
            add.Parameters.AddWithValue("id", role.Id);
            add.Parameters.AddWithValue("permission", permission);
            await add.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Deletes the role with the given id.
    /// </summary>
    public async Task DeleteRoleAsync(Guid id, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from admin_roles where id = @id");
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Returns all AD group to role mappings.
    /// </summary>
    public async Task<IReadOnlyList<GroupMapping>> ListGroupMappingsAsync(CancellationToken ct = default)
    {
        var groups = new List<(string Key, string Name)>();
        await using (var command = _db.CreateCommand("select group_key, display_name from admin_groups order by group_key"))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                groups.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var roles = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        await using (var command = _db.CreateCommand("select group_key, role_id from admin_group_roles"))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var key = reader.GetString(0);
                if (!roles.TryGetValue(key, out var list))
                {
                    roles[key] = list = new List<Guid>();
                }
                list.Add(reader.GetGuid(1));
            }
        }

        return groups.Select(g => new GroupMapping(g.Key, g.Name, roles.TryGetValue(g.Key, out var r) ? r : new List<Guid>())).ToList();
    }

    /// <summary>
    /// Inserts or updates an AD group mapping.
    /// </summary>
    public async Task SaveGroupMappingAsync(GroupMapping mapping, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var upsert = new NpgsqlCommand(
            "insert into admin_groups (group_key, display_name) values (@key, @name) on conflict (group_key) do update set display_name = excluded.display_name", connection, transaction))
        {
            upsert.Parameters.AddWithValue("key", mapping.Group);
            upsert.Parameters.AddWithValue("name", mapping.DisplayName);
            await upsert.ExecuteNonQueryAsync(ct);
        }
        await using (var clear = new NpgsqlCommand("delete from admin_group_roles where group_key = @key", connection, transaction))
        {
            clear.Parameters.AddWithValue("key", mapping.Group);
            await clear.ExecuteNonQueryAsync(ct);
        }
        foreach (var roleId in mapping.RoleIds.Distinct())
        {
            await using var add = new NpgsqlCommand("insert into admin_group_roles (group_key, role_id) values (@key, @role)", connection, transaction);
            add.Parameters.AddWithValue("key", mapping.Group);
            add.Parameters.AddWithValue("role", roleId);
            await add.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Deletes the mapping for the given AD group.
    /// </summary>
    public async Task DeleteGroupMappingAsync(string group, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from admin_groups where group_key = @key");
        command.Parameters.AddWithValue("key", group);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts an administration audit row with time, actor, action, target and detail into the PostgreSQL admin_audit table.
    /// </summary>
    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "insert into admin_audit (at, actor, action, target, detail) values (@at, @actor, @action, @target, @detail)");
        command.Parameters.AddWithValue("at", entry.At.ToUniversalTime());
        command.Parameters.AddWithValue("actor", entry.Actor);
        command.Parameters.AddWithValue("action", entry.Action);
        command.Parameters.AddWithValue("target", entry.Target);
        command.Parameters.AddWithValue("detail", entry.Detail);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Pages audit entries oldest to newest with a keyed (time, id) cursor for exports, accepting only a safe action prefix.
    /// </summary>
    public async Task<IReadOnlyList<CrmNlQuery.Application.Export.Models.AuditPageItem>> PageAuditAsync(DateTimeOffset from, DateTimeOffset to, string? actor, string? actionPrefix,
        CrmNlQuery.Application.Export.Models.AuditCursor? after, int size, CancellationToken ct = default)
    {
        var prefix = (actionPrefix ?? "").Trim().ToLowerInvariant();
        if (prefix.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-')))
        {
            return new List<CrmNlQuery.Application.Export.Models.AuditPageItem>();
        }
        await using var command = _db.CreateCommand(
            @"select id, at, actor, action, target, detail from admin_audit
              where at >= @from and at < @to
                and (@actor::text is null or lower(actor) = lower(@actor::text))
                and (@prefix::text is null or lower(left(action, char_length(@prefix::text))) = @prefix::text)
                and (@afterAt::timestamptz is null or (at, id) > (@afterAt::timestamptz, @afterId::bigint))
              order by at, id limit @take");
        command.Parameters.AddWithValue("from", from.ToUniversalTime());
        command.Parameters.AddWithValue("to", to.ToUniversalTime());
        command.Parameters.AddWithValue("actor", string.IsNullOrWhiteSpace(actor) ? DBNull.Value : actor.Trim());
        command.Parameters.AddWithValue("prefix", prefix.Length == 0 ? DBNull.Value : prefix);
        command.Parameters.AddWithValue("afterAt", after is null ? DBNull.Value : after.At.ToUniversalTime());
        command.Parameters.AddWithValue("afterId", after is null ? DBNull.Value : after.Id);
        command.Parameters.AddWithValue("take", Math.Max(1, size));
        var items = new List<CrmNlQuery.Application.Export.Models.AuditPageItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new CrmNlQuery.Application.Export.Models.AuditPageItem(reader.GetInt64(0),
                new AuditEntry(reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))));
        }
        return items;
    }

    /// <summary>
    /// Returns the most recent audit entries up to the given count.
    /// </summary>
    public async Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "select at, actor, action, target, detail from admin_audit order by at desc, id desc limit @limit");
        command.Parameters.AddWithValue("limit", limit);
        var entries = new List<AuditEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(new AuditEntry(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }
        return entries;
    }

    /// <summary>
    /// Tells whether the actor has any audit entry.
    /// </summary>
    public async Task<bool> HasAuditByActorAsync(string account, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("select exists (select 1 from admin_audit where actor = @actor)");
        command.Parameters.AddWithValue("actor", account);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }
}
