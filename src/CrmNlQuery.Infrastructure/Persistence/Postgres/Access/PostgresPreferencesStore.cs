using Npgsql;
using CrmNlQuery.Application.Preferences.Abstractions;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Stores user preferences in PostgreSQL with all values passed as parameters.
/// </summary>
public sealed class PostgresPreferencesStore : IPreferencesStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresPreferencesStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the preferences store and ensures its connection works.
    /// </summary>
    public static async Task<PostgresPreferencesStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresPreferencesStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Returns the display name saved for a user, if any.
    /// </summary>
    public async Task<string?> GetDisplayNameAsync(Guid owner, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("select display_name from user_preferences where owner = @owner");
        command.Parameters.AddWithValue("owner", owner);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : (string)value;
    }

    /// <summary>
    /// Returns the saved UI language for an account, if any.
    /// </summary>
    public async Task<string?> GetLanguageAsync(string account, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("select language from user_language where account = @account");
        command.Parameters.AddWithValue("account", account);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : (string)value;
    }

    /// <summary>
    /// Saves or clears the UI language for an account.
    /// </summary>
    public async Task SetLanguageAsync(string account, string? language, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(language))
        {
            await using var delete = _db.CreateCommand("delete from user_language where account = @account");
            delete.Parameters.AddWithValue("account", account);
            await delete.ExecuteNonQueryAsync(ct);
            return;
        }
        await using var command = _db.CreateCommand(
            @"insert into user_language (account, language, updated_at) values (@account, @language, now())
              on conflict (account) do update set language = @language, updated_at = now()");
        command.Parameters.AddWithValue("account", account);
        command.Parameters.AddWithValue("language", language);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Saves or clears the display name for a user.
    /// </summary>
    public async Task SetDisplayNameAsync(Guid owner, string? displayName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            await using var delete = _db.CreateCommand("delete from user_preferences where owner = @owner");
            delete.Parameters.AddWithValue("owner", owner);
            await delete.ExecuteNonQueryAsync(ct);
            return;
        }
        await using var command = _db.CreateCommand(
            @"insert into user_preferences (owner, display_name, updated_at) values (@owner, @name, now())
              on conflict (owner) do update set display_name = @name, updated_at = now()");
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("name", displayName);
        await command.ExecuteNonQueryAsync(ct);
    }
}
