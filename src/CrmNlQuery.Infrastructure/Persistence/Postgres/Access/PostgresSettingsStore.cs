using Npgsql;
using CrmNlQuery.Application.Operations.Abstractions;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Stores panel setting overrides in PostgreSQL; values always go as parameters.
/// </summary>
public sealed class PostgresSettingsStore : ISettingsStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresSettingsStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the settings store, applying the schema first.
    /// </summary>
    public static async Task<PostgresSettingsStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresSettingsStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Reads all runtime setting overrides from the PostgreSQL runtime_settings table as a key-to-value dictionary.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("select key, value from runtime_settings");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            all[reader.GetString(0)] = reader.GetString(1);
        }
        return all;
    }

    /// <summary>
    /// Stores a setting override with the actor and time.
    /// </summary>
    public async Task SetAsync(string key, string value, string actor, DateTimeOffset at, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"insert into runtime_settings (key, value, updated_by, updated_at) values (@key, @value, @by, @at)
              on conflict (key) do update set value = @value, updated_by = @by, updated_at = @at");
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("value", value);
        command.Parameters.AddWithValue("by", actor);
        command.Parameters.AddWithValue("at", at.ToUniversalTime());
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deletes one runtime setting override by key from PostgreSQL, so the setting falls back to file or default value.
    /// </summary>
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from runtime_settings where key = @key");
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(ct);
    }
}
