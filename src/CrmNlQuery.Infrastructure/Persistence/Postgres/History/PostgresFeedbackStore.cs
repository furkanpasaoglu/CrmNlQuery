using Npgsql;
using CrmNlQuery.Application.Feedback.Abstractions;
using CrmNlQuery.Domain.Feedback.Entities;
using CrmNlQuery.Application.Feedback.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.History;

/// <summary>
/// Stores feedback votes and shared candidates in PostgreSQL; votes carry no question text.
/// </summary>
public sealed class PostgresFeedbackStore : IFeedbackStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresFeedbackStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the store and ensures its connection is ready.
    /// </summary>
    public static async Task<PostgresFeedbackStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresFeedbackStore(NpgsqlDataSource.Create(connectionString));
    }

    private static bool TryParseId(string? id, out Guid guid)
    {
        guid = Guid.Empty;
        return id is { Length: 32 } && Guid.TryParseExact(id, "N", out guid);
    }

    /// <summary>
    /// Inserts or updates a vote and optionally its shared candidate.
    /// </summary>
    public async Task<bool> UpsertAsync(FeedbackVote vote, FeedbackCandidate? candidate, CancellationToken ct = default)
    {
        if (!TryParseId(vote.ResultId, out var result))
        {
            return false;
        }
        try
        {
            return await UpsertCoreAsync(result, vote, candidate, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return false;
        }
    }

    private async Task<bool> UpsertCoreAsync(Guid result, FeedbackVote vote, FeedbackCandidate? candidate, CancellationToken ct)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var upsert = new NpgsqlCommand(
            @"insert into feedback (result_id, owner, rating, reason, shared, tables, steps, duration_ms, created_at, updated_at)
              values (@id, @owner, @rating, @reason, @shared, @tables, @steps, @duration, @at, @at)
              on conflict (result_id) do update set rating = excluded.rating, reason = excluded.reason, shared = excluded.shared,
                  tables = excluded.tables, steps = excluded.steps, duration_ms = excluded.duration_ms, updated_at = excluded.updated_at
              where feedback.owner = excluded.owner", connection, transaction))
        {
            upsert.Parameters.AddWithValue("id", result);
            upsert.Parameters.AddWithValue("owner", vote.Owner);
            upsert.Parameters.AddWithValue("rating", vote.Rating);
            upsert.Parameters.AddWithValue("reason", vote.Reason is null ? DBNull.Value : vote.Reason);
            upsert.Parameters.AddWithValue("shared", vote.Shared);
            upsert.Parameters.AddWithValue("tables", vote.Tables.ToArray());
            upsert.Parameters.AddWithValue("steps", vote.Steps is int s ? s : DBNull.Value);
            upsert.Parameters.AddWithValue("duration", vote.DurationMs is int d ? d : DBNull.Value);
            upsert.Parameters.AddWithValue("at", vote.UpdatedAt);
            if (await upsert.ExecuteNonQueryAsync(ct) == 0)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
        }
        if (candidate is null)
        {
            await using var delete = new NpgsqlCommand("delete from feedback_candidates where result_id = @id and owner = @owner", connection, transaction);
            delete.Parameters.AddWithValue("id", result);
            delete.Parameters.AddWithValue("owner", vote.Owner);
            await delete.ExecuteNonQueryAsync(ct);
        }
        else
        {
            await using var insert = new NpgsqlCommand(
                @"insert into feedback_candidates (result_id, owner, question, explanation, reason, created_at)
                  values (@id, @owner, @question, @explanation, @reason, @at)
                  on conflict (result_id) do update set question = excluded.question, explanation = excluded.explanation, reason = excluded.reason
                  where feedback_candidates.owner = excluded.owner", connection, transaction);
            insert.Parameters.AddWithValue("id", result);
            insert.Parameters.AddWithValue("owner", vote.Owner);
            insert.Parameters.AddWithValue("question", candidate.Question);
            insert.Parameters.AddWithValue("explanation", candidate.Explanation);
            insert.Parameters.AddWithValue("reason", candidate.Reason is null ? DBNull.Value : candidate.Reason);
            insert.Parameters.AddWithValue("at", candidate.CreatedAt);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Removes the owner vote and its candidate for a result.
    /// </summary>
    public async Task<bool> RemoveAsync(Guid owner, string resultId, CancellationToken ct = default)
    {
        if (!TryParseId(resultId, out var id))
        {
            return false;
        }
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var candidate = new NpgsqlCommand("delete from feedback_candidates where result_id = @id and owner = @owner", connection, transaction))
        {
            candidate.Parameters.AddWithValue("id", id);
            candidate.Parameters.AddWithValue("owner", owner);
            await candidate.ExecuteNonQueryAsync(ct);
        }
        int removed;
        await using (var vote = new NpgsqlCommand("delete from feedback where result_id = @id and owner = @owner", connection, transaction))
        {
            vote.Parameters.AddWithValue("id", id);
            vote.Parameters.AddWithValue("owner", owner);
            removed = await vote.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return removed > 0;
    }

    /// <summary>
    /// Returns the owner vote states for the given result ids.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, FeedbackState>> GetStatesAsync(Guid owner, IReadOnlyList<string> resultIds, CancellationToken ct = default)
    {
        var ids = resultIds.Select(i => TryParseId(i, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToArray();
        var map = new Dictionary<string, FeedbackState>(StringComparer.Ordinal);
        if (ids.Length == 0)
        {
            return map;
        }
        await using var command = _db.CreateCommand(
            @"select f.result_id, f.rating, f.reason, (f.shared and c.result_id is not null)
              from feedback f left join feedback_candidates c on c.result_id = f.result_id
              where f.owner = @owner and f.result_id = any(@ids)");
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("ids", ids);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            map[reader.GetGuid(0).ToString("N")] = new FeedbackState(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetBoolean(3));
        }
        return map;
    }

    /// <summary>
    /// Summarizes votes for a date range.
    /// </summary>
    public async Task<FeedbackSummary> SummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        int up = 0, down = 0, single = 0, multi = 0;
        await using (var totals = _db.CreateCommand(
            @"select count(*) filter (where rating = 'up'), count(*) filter (where rating = 'down'),
                     count(*) filter (where rating = 'down' and coalesce(steps, 1) <= 1), count(*) filter (where rating = 'down' and steps > 1)
              from feedback where updated_at >= @from and updated_at < @to"))
        {
            totals.Parameters.AddWithValue("from", from);
            totals.Parameters.AddWithValue("to", to);
            await using var reader = await totals.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                up = (int)reader.GetInt64(0);
                down = (int)reader.GetInt64(1);
                single = (int)reader.GetInt64(2);
                multi = (int)reader.GetInt64(3);
            }
        }
        var reasons = await CountsAsync(
            "select reason, count(*) from feedback where rating = 'down' and reason is not null and updated_at >= @from and updated_at < @to group by reason order by count(*) desc, reason", from, to, ct);
        var tables = await CountsAsync(
            "select t, count(*) from feedback, unnest(tables) as t where rating = 'down' and updated_at >= @from and updated_at < @to group by t order by count(*) desc, t limit 10", from, to, ct);
        var days = new List<FeedbackDay>();
        await using (var perDay = _db.CreateCommand(
            @"select (updated_at at time zone 'utc')::date, count(*) filter (where rating = 'up'), count(*) filter (where rating = 'down')
              from feedback where updated_at >= @from and updated_at < @to group by 1 order by 1"))
        {
            perDay.Parameters.AddWithValue("from", from);
            perDay.Parameters.AddWithValue("to", to);
            await using var reader = await perDay.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                days.Add(new FeedbackDay(DateOnly.FromDateTime(reader.GetDateTime(0)), (int)reader.GetInt64(1), (int)reader.GetInt64(2)));
            }
        }
        return new FeedbackSummary(up, down, reasons, tables, single, multi, days);
    }

    private async Task<IReadOnlyList<FeedbackCount>> CountsAsync(string sql, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var list = new List<FeedbackCount>();
        await using var command = _db.CreateCommand(sql);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new FeedbackCount(reader.GetString(0), (int)reader.GetInt64(1)));
        }
        return list;
    }

    /// <summary>
    /// Reads the shared golden-set candidates from PostgreSQL, newest first, up to the requested number.
    /// </summary>
    public async Task<IReadOnlyList<FeedbackCandidate>> ListCandidatesAsync(int take, CancellationToken ct = default)
    {
        var list = new List<FeedbackCandidate>();
        await using var command = _db.CreateCommand("select result_id, question, explanation, reason, created_at from feedback_candidates order by created_at desc, result_id limit @take");
        command.Parameters.AddWithValue("take", Math.Max(1, take));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new FeedbackCandidate(reader.GetGuid(0).ToString("N"), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return list;
    }

    /// <summary>
    /// Deletes the shared candidate of a result.
    /// </summary>
    public async Task<bool> DeleteCandidateAsync(string resultId, CancellationToken ct = default)
    {
        if (!TryParseId(resultId, out var id))
        {
            return false;
        }
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        int removed;
        await using (var delete = new NpgsqlCommand("delete from feedback_candidates where result_id = @id", connection, transaction))
        {
            delete.Parameters.AddWithValue("id", id);
            removed = await delete.ExecuteNonQueryAsync(ct);
        }
        await using (var mark = new NpgsqlCommand("update feedback set shared = false where result_id = @id", connection, transaction))
        {
            mark.Parameters.AddWithValue("id", id);
            await mark.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return removed > 0;
    }

    /// <summary>
    /// Deletes old votes and candidates by their cutoff dates.
    /// </summary>
    public async Task<int> PurgeAsync(DateTimeOffset? voteCutoff, DateTimeOffset? candidateCutoff, CancellationToken ct = default)
    {
        var removed = 0;
        if (candidateCutoff is { } cc)
        {
            await using var command = _db.CreateCommand("delete from feedback_candidates where created_at < @cutoff");
            command.Parameters.AddWithValue("cutoff", cc);
            removed += await command.ExecuteNonQueryAsync(ct);
        }
        if (voteCutoff is { } vc)
        {
            await using var votes = _db.CreateCommand("delete from feedback where updated_at < @cutoff");
            votes.Parameters.AddWithValue("cutoff", vc);
            removed += await votes.ExecuteNonQueryAsync(ct);
        }
        return removed;
    }
}
