using Npgsql;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Observability;

/// <summary>
/// PostgreSQL log store where user input such as search text always goes as a parameter.
/// </summary>
public sealed class PostgresLogStore : ILogStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresLogStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Applies schema migrations and returns the log store using pooled connections.
    /// </summary>
    public static async Task<PostgresLogStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresLogStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Inserts a batch of application log entries into PostgreSQL in one transaction, doing nothing for an empty batch.
    /// </summary>
    public async Task AddAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
        {
            return;
        }
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var e in entries)
        {
            await using var command = new NpgsqlCommand(
                "insert into app_logs (at, level, message, exception, request_id) values (@at, @level, @message, @exception, @request)",
                connection, transaction);
            command.Parameters.AddWithValue("at", e.At.ToUniversalTime());
            command.Parameters.AddWithValue("level", e.Level);
            command.Parameters.AddWithValue("message", e.Message);
            command.Parameters.AddWithValue("exception", (object?)e.Exception ?? DBNull.Value);
            command.Parameters.AddWithValue("request", (object?)e.RequestId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Queries log entries by the given filter.
    /// </summary>
    public async Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken ct = default)
    {
        var sql = "select at, level, message, exception, request_id from app_logs where level >= @min";
        if (query.From is not null)
        {
            sql += " and at >= @from";
        }
        var hasText = !string.IsNullOrEmpty(query.Text);
        if (hasText)
        {
            sql += " and (position(lower(@text) in lower(message)) > 0" +
                   " or position(lower(@text) in lower(coalesce(exception, ''))) > 0" +
                   " or lower(coalesce(request_id, '')) = lower(@text))";
        }
        sql += " order by at desc, id desc limit @take";

        await using var command = _db.CreateCommand(sql);
        command.Parameters.AddWithValue("min", query.MinLevel);
        if (query.From is not null)
        {
            command.Parameters.AddWithValue("from", query.From.Value.ToUniversalTime());
        }
        if (hasText)
        {
            command.Parameters.AddWithValue("text", query.Text!);
        }
        command.Parameters.AddWithValue("take", query.Take);

        var found = new List<LogEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            found.Add(new LogEntry(
                reader.GetFieldValue<DateTimeOffset>(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return found;
    }

    /// <summary>
    /// Deletes log entries older than the cutoff.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from app_logs where at < @cutoff");
        command.Parameters.AddWithValue("cutoff", cutoff.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(ct);
    }
}
