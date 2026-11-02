using Npgsql;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres;

/// <summary>
/// Checks at startup whether PostgreSQL is ready by opening a connection and running select 1.
/// </summary>
public static class PostgresReadiness
{
    /// <summary>
    /// Creates a readiness probe from a connection string, failing at once if the string is malformed.
    /// </summary>
    public static Func<CancellationToken, Task> CreateProbe(string connectionString, int connectTimeoutSeconds = 5)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            Timeout = connectTimeoutSeconds,
        };
        var probeConnection = builder.ConnectionString;
        return async ct =>
        {
            await using var connection = new NpgsqlConnection(probeConnection);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("select 1", connection);
            await command.ExecuteScalarAsync(ct);
        };
    }

    /// <summary>
    /// Tells whether an exception is temporary, since permanent errors like wrong credentials are not retried.
    /// </summary>
    public static bool IsTransient(Exception ex)
    {
        if (ex is PostgresException pg)
        {
            return !(pg.SqlState.StartsWith("28", StringComparison.Ordinal) || pg.SqlState == "3D000");
        }
        return true;
    }
}
