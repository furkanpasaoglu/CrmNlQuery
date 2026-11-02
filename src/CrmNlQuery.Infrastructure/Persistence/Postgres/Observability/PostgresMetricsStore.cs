using Npgsql;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Observability;

/// <summary>
/// Stores AI call and request metrics in PostgreSQL with parameterized SQL and UTC times.
/// </summary>
public sealed class PostgresMetricsStore : IMetricsStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresMetricsStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Applies the schema migrations and returns a store over a pooled connection.
    /// </summary>
    public static async Task<PostgresMetricsStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresMetricsStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Inserts one model call record into the PostgreSQL ai_calls table, including tokens, outcome and prompt size, with no question text.
    /// </summary>
    public async Task RecordAiCallAsync(AiCallRecord r, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "insert into ai_calls (request_id, at, purpose, provider, model, duration_ms, input_tokens, output_tokens, outcome, error_type, prompt_chars, est_tokens) " +
            "values (@request, @at, @purpose, @provider, @model, @ms, @input, @output, @outcome, @error, @chars, @est)");
        command.Parameters.AddWithValue("request", r.RequestId);
        command.Parameters.AddWithValue("at", r.At.ToUniversalTime());
        command.Parameters.AddWithValue("purpose", r.Purpose);
        command.Parameters.AddWithValue("provider", r.Provider);
        command.Parameters.AddWithValue("model", r.Model);
        command.Parameters.AddWithValue("ms", r.DurationMs);
        command.Parameters.AddWithValue("input", (object?)r.InputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("output", (object?)r.OutputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("outcome", r.Outcome);
        command.Parameters.AddWithValue("error", (object?)r.ErrorType ?? DBNull.Value);
        command.Parameters.AddWithValue("chars", (object?)r.PromptChars ?? DBNull.Value);
        command.Parameters.AddWithValue("est", (object?)r.EstTokens ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts one request record into the PostgreSQL requests table, including duration, outcome, knowledge pieces and follow-up flag, with no question text.
    /// </summary>
    public async Task RecordRequestAsync(RequestRecord r, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "insert into requests (id, at, owner, duration_ms, outcome, error_type, knowledge_pieces, follow_up) values (@id, @at, @owner, @ms, @outcome, @error, @pieces, @follow)");
        command.Parameters.AddWithValue("id", r.Id);
        command.Parameters.AddWithValue("at", r.At.ToUniversalTime());
        command.Parameters.AddWithValue("owner", (object?)r.User ?? DBNull.Value);
        command.Parameters.AddWithValue("ms", r.DurationMs);
        command.Parameters.AddWithValue("outcome", r.Outcome);
        command.Parameters.AddWithValue("error", (object?)r.ErrorType ?? DBNull.Value);
        command.Parameters.AddWithValue("pieces", (object?)r.KnowledgePieces ?? DBNull.Value);
        command.Parameters.AddWithValue("follow", (object?)r.FollowUp ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Aggregates request and model-call metrics in PostgreSQL for the given UTC time range into the summary shown on the dashboard.
    /// </summary>
    public async Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var f = from.ToUniversalTime();
        var t = to.ToUniversalTime();

        int requests, answers, questions, failures, knowledgeSearched, knowledgeUsed, followUps, cancelled;
        long knowledgePieces;
        double requestP50, requestP95;
        await using (var c = Window("select count(*), count(*) filter (where outcome = 'answer'), count(*) filter (where outcome = 'question'), " +
            "count(*) filter (where outcome = 'failure'), coalesce(percentile_cont(0.5) within group (order by duration_ms), 0), " +
            "coalesce(percentile_cont(0.95) within group (order by duration_ms), 0), " +
            "count(*) filter (where knowledge_pieces is not null), count(*) filter (where knowledge_pieces > 0), coalesce(sum(knowledge_pieces), 0), " +
            "count(*) filter (where follow_up), count(*) filter (where outcome = 'cancelled') " +
            "from requests where at >= @from and at < @to", f, t))
        await using (var r = await c.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            requests = (int)r.GetInt64(0);
            answers = (int)r.GetInt64(1);
            questions = (int)r.GetInt64(2);
            failures = (int)r.GetInt64(3);
            requestP50 = r.GetDouble(4);
            requestP95 = r.GetDouble(5);
            knowledgeSearched = (int)r.GetInt64(6);
            knowledgeUsed = (int)r.GetInt64(7);
            knowledgePieces = r.GetInt64(8);
            followUps = (int)r.GetInt64(9);
            cancelled = (int)r.GetInt64(10);
        }

        int aiCalls, aiErrors;
        long inputTokens, outputTokens;
        await using (var c = Window("select count(*), count(*) filter (where outcome = 'error'), coalesce(sum(input_tokens), 0), " +
            "coalesce(sum(output_tokens), 0) from ai_calls where at >= @from and at < @to", f, t))
        await using (var r = await c.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            aiCalls = (int)r.GetInt64(0);
            aiErrors = (int)r.GetInt64(1);
            inputTokens = r.GetInt64(2);
            outputTokens = r.GetInt64(3);
        }

        var requestDays = new Dictionary<DateOnly, (int Requests, int Failures)>();
        await using (var c = Window("select (at at time zone 'UTC')::date, count(*), count(*) filter (where outcome = 'failure') " +
            "from requests where at >= @from and at < @to group by 1", f, t))
        await using (var r = await c.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                requestDays[r.GetFieldValue<DateOnly>(0)] = ((int)r.GetInt64(1), (int)r.GetInt64(2));
            }
        }
        var callDays = new Dictionary<DateOnly, (int Calls, long Input, long Output)>();
        await using (var c = Window("select (at at time zone 'UTC')::date, count(*), coalesce(sum(input_tokens), 0), coalesce(sum(output_tokens), 0) " +
            "from ai_calls where at >= @from and at < @to group by 1", f, t))
        await using (var r = await c.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                callDays[r.GetFieldValue<DateOnly>(0)] = ((int)r.GetInt64(1), r.GetInt64(2), r.GetInt64(3));
            }
        }
        var days = requestDays.Keys.Concat(callDays.Keys).Distinct().OrderBy(d => d)
            .Select(d =>
            {
                requestDays.TryGetValue(d, out var rq);
                callDays.TryGetValue(d, out var cl);
                return new DayPoint(d, rq.Requests, rq.Failures, cl.Calls, cl.Input, cl.Output);
            }).ToList();

        var latency = new List<LatencyPoint>();
        await using (var c = Window("select purpose, count(*), count(*) filter (where outcome = 'error'), " +
            "percentile_cont(0.5) within group (order by duration_ms), percentile_cont(0.95) within group (order by duration_ms), " +
            "avg(prompt_chars), avg(est_tokens) filter (where prompt_chars is not null) " +
            "from ai_calls where at >= @from and at < @to group by purpose order by purpose", f, t))
        await using (var r = await c.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                latency.Add(new LatencyPoint(r.GetString(0), (int)r.GetInt64(1), (int)r.GetInt64(2), r.GetDouble(3), r.GetDouble(4),
                    r.IsDBNull(5) ? null : Convert.ToDouble(r.GetValue(5)), r.IsDBNull(6) ? null : Convert.ToDouble(r.GetValue(6))));
            }
        }

        var models = new List<ModelUsage>();
        await using (var c = Window("select provider, model, count(*), coalesce(sum(input_tokens), 0), coalesce(sum(output_tokens), 0) " +
            "from ai_calls where at >= @from and at < @to group by provider, model order by count(*) desc, model", f, t))
        await using (var r = await c.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                models.Add(new ModelUsage(r.GetString(0), r.GetString(1), (int)r.GetInt64(2), r.GetInt64(3), r.GetInt64(4)));
            }
        }

        var failuresByType = await CountByAsync("select coalesce(error_type, 'other'), count(*) from requests " +
            "where outcome = 'failure' and at >= @from and at < @to group by 1 order by 2 desc, 1", f, t, ct);
        var aiErrorsByType = await CountByAsync("select coalesce(error_type, 'other'), count(*) from ai_calls " +
            "where outcome = 'error' and at >= @from and at < @to group by 1 order by 2 desc, 1", f, t, ct);

        var users = new List<UserUsage>();
        await using (var c = Window("select owner, count(*) from requests where owner is not null and at >= @from and at < @to " +
            "group by owner order by 2 desc limit 20", f, t))
        await using (var r = await c.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                users.Add(new UserUsage(r.GetGuid(0), (int)r.GetInt64(1)));
            }
        }

        return new MetricsSummary(requests, answers, questions, failures, aiCalls, aiErrors, inputTokens, outputTokens,
            requestP50, requestP95, days, latency, models, failuresByType, aiErrorsByType, users,
            knowledgeSearched, knowledgeUsed, knowledgePieces, followUps, cancelled);
    }

    /// <summary>
    /// Deletes ai_calls and requests rows older than the cutoff from PostgreSQL and returns the total number removed.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var removed = 0;
        foreach (var table in new[] { "ai_calls", "requests" })
        {
            await using var command = _db.CreateCommand($"delete from {table} where at < @cutoff");
            command.Parameters.AddWithValue("cutoff", cutoff.ToUniversalTime());
            removed += await command.ExecuteNonQueryAsync(ct);
        }
        return removed;
    }

    private NpgsqlCommand Window(string sql, DateTimeOffset from, DateTimeOffset to)
    {
        var command = _db.CreateCommand(sql);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        return command;
    }

    private async Task<IReadOnlyList<CountBy>> CountByAsync(string sql, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var list = new List<CountBy>();
        await using var command = Window(sql, from, to);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CountBy(reader.GetString(0), (int)reader.GetInt64(1)));
        }
        return list;
    }
}
