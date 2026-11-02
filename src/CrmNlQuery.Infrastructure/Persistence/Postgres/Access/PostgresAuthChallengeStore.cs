using CrmNlQuery.Application.Access.Abstractions;
using Npgsql;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// PostgreSQL sign-in challenges (auth_challenges): only hashes are stored and a challenge is used with one atomic update, so it works across several service instances.
/// </summary>
public sealed class PostgresAuthChallengeStore : IAuthChallengeStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresAuthChallengeStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Runs the schema migrations and opens the store.
    /// </summary>
    public static async Task<PostgresAuthChallengeStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresAuthChallengeStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Stores a new challenge and removes challenges that expired more than a day ago.
    /// </summary>
    public async Task AddAsync(string nonceHash, string secretHash, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "delete from auth_challenges where expires_at < @old; " +
            "insert into auth_challenges (nonce_hash, secret_hash, expires_at, used) values (@n, @s, @e, false)");
        command.Parameters.AddWithValue("old", expiresAt.AddDays(-1));
        command.Parameters.AddWithValue("n", nonceHash);
        command.Parameters.AddWithValue("s", secretHash);
        command.Parameters.AddWithValue("e", expiresAt);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Uses the challenge once if the secret hash matches and it has not expired.
    /// </summary>
    public async Task<bool> TryConsumeAsync(string nonceHash, string secretHash, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "update auth_challenges set used = true where nonce_hash = @n and secret_hash = @s and used = false and expires_at > @now");
        command.Parameters.AddWithValue("n", nonceHash);
        command.Parameters.AddWithValue("s", secretHash);
        command.Parameters.AddWithValue("now", now);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>
    /// Removes challenges that expired before the given time.
    /// </summary>
    public async Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from auth_challenges where expires_at < @before");
        command.Parameters.AddWithValue("before", before);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
