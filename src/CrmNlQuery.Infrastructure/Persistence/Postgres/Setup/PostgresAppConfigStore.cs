using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;
using Npgsql;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Setup;

/// <summary>
/// PostgreSQL setup settings (app_config); secrets arrive already encrypted and are stored as given.
/// </summary>
public sealed class PostgresAppConfigStore : IAppConfigStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresAppConfigStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Runs the schema migrations and opens the store.
    /// </summary>
    public static async Task<PostgresAppConfigStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresAppConfigStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Returns every stored setting ordered by key.
    /// </summary>
    public async Task<IReadOnlyList<AppConfigEntry>> ListAsync(CancellationToken ct = default)
    {
        var list = new List<AppConfigEntry>();
        await using var command = _db.CreateCommand("select key, value, secret from app_config order by key");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { list.Add(new AppConfigEntry(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2))); }
        return list;
    }

    /// <summary>
    /// Adds or replaces the given settings in one transaction.
    /// </summary>
    public async Task SetManyAsync(IReadOnlyList<AppConfigEntry> entries, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var entry in entries)
        {
            await using var command = new NpgsqlCommand(
                "insert into app_config (key, value, secret, updated_at) values (@k, @v, @s, now()) " +
                "on conflict (key) do update set value = @v, secret = @s, updated_at = now()", connection, transaction);
            command.Parameters.AddWithValue("k", entry.Key);
            command.Parameters.AddWithValue("v", entry.Value);
            command.Parameters.AddWithValue("s", entry.Secret);
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Removes every stored setting.
    /// </summary>
    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from app_config");
        await command.ExecuteNonQueryAsync(ct);
    }
}
