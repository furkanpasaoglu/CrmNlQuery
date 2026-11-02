using Npgsql;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Stores the data access log in PostgreSQL, append-only: no update or delete methods and database triggers reject changes.
/// </summary>
public sealed class PostgresAccessLogStore : IAccessLogStore, CrmNlQuery.Application.Export.Abstractions.IAccessLogPager
{
    private readonly NpgsqlDataSource _db;

    private PostgresAccessLogStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the PostgreSQL access log store from a connection string.
    /// </summary>
    public static async Task<PostgresAccessLogStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresAccessLogStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Appends one data access record to the log.
    /// </summary>
    public async Task AddAsync(DataAccessRecord record, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "insert into data_access_log (id, at, user_id, source, tables, rows, outcome, request_id) values (@id, @at, @user, @source, @tables, @rows, @outcome, @request)");
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("at", record.At.ToUniversalTime());
        command.Parameters.AddWithValue("user", record.User);
        command.Parameters.AddWithValue("source", record.Source);
        command.Parameters.AddWithValue("tables", string.Join(",", record.Tables));
        command.Parameters.AddWithValue("rows", record.Rows);
        command.Parameters.AddWithValue("outcome", record.Outcome);
        command.Parameters.AddWithValue("request", record.RequestId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Pages access log rows oldest to newest with a keyset cursor on time and id, for exports.
    /// </summary>
    public async Task<IReadOnlyList<DataAccessRecord>> PageAsync(DataAccessQuery query, CrmNlQuery.Application.Export.Models.AccessCursor? after, int size, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"select id, at, user_id, source, tables, rows, outcome, request_id from data_access_log
              where at >= @from and at < @to
                and (@user::uuid is null or user_id = @user::uuid)
                and (@table::text is null or @table::text = any(string_to_array(tables, ',')))
                and (@source::text is null or source = @source::text)
                and (@afterAt::timestamptz is null or (at, id) > (@afterAt::timestamptz, @afterId::uuid))
              order by at, id limit @take");
        command.Parameters.AddWithValue("from", query.From.ToUniversalTime());
        command.Parameters.AddWithValue("to", query.To.ToUniversalTime());
        command.Parameters.AddWithValue("user", query.User is { } u ? u : DBNull.Value);
        var table = (query.Table ?? "").Trim().ToLowerInvariant();
        command.Parameters.AddWithValue("table", table.Length == 0 ? DBNull.Value : table);
        var source = (query.Source ?? "").Trim();
        command.Parameters.AddWithValue("source", source.Length == 0 ? DBNull.Value : source);
        command.Parameters.AddWithValue("afterAt", after is null ? DBNull.Value : after.At.ToUniversalTime());
        command.Parameters.AddWithValue("afterId", after is null ? DBNull.Value : after.Id);
        command.Parameters.AddWithValue("take", Math.Max(1, size));

        return await ReadRecordsAsync(command, ct);
    }

    /// <summary>
    /// Queries access log records using the given filters.
    /// </summary>
    public async Task<IReadOnlyList<DataAccessRecord>> QueryAsync(DataAccessQuery query, CancellationToken ct = default)
    {
        var take = Math.Clamp(query.Take, 1, 500);
        await using var command = _db.CreateCommand(
            @"select id, at, user_id, source, tables, rows, outcome, request_id from data_access_log
              where at >= @from and at < @to
                and (@user::uuid is null or user_id = @user::uuid)
                and (@table::text is null or @table::text = any(string_to_array(tables, ',')))
              order by at desc limit @take");
        command.Parameters.AddWithValue("from", query.From.ToUniversalTime());
        command.Parameters.AddWithValue("to", query.To.ToUniversalTime());
        command.Parameters.AddWithValue("user", query.User is { } u ? u : DBNull.Value);
        var table = (query.Table ?? "").Trim().ToLowerInvariant();
        command.Parameters.AddWithValue("table", table.Length == 0 ? DBNull.Value : table);
        command.Parameters.AddWithValue("take", take);

        return await ReadRecordsAsync(command, ct);
    }

    private static async Task<IReadOnlyList<DataAccessRecord>> ReadRecordsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var list = new List<DataAccessRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DataAccessRecord(reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetString(4).Split(',', StringSplitOptions.RemoveEmptyEntries), reader.GetInt32(5), reader.GetString(6), reader.GetGuid(7)));
        }
        return list;
    }

    /// <summary>
    /// Deletes records older than a cutoff for retention, using a transaction-scoped session flag to let the trigger allow it.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var flag = new NpgsqlCommand("select set_config('nlquery.purge', 'on', true)", connection, transaction))
        {
            await flag.ExecuteNonQueryAsync(ct);
        }
        int removed;
        await using (var delete = new NpgsqlCommand("delete from data_access_log where at < @cutoff", connection, transaction))
        {
            delete.Parameters.AddWithValue("cutoff", cutoff.ToUniversalTime());
            removed = await delete.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return removed;
    }
}
