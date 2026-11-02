using Npgsql;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Quality;

/// <summary>
/// Stores quality check runs and results in PostgreSQL, without ever storing row data, using parameterized values.
/// </summary>
public sealed class PostgresQualityRunStore : IQualityRunStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresQualityRunStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the PostgreSQL quality run store and ensures its schema is ready.
    /// </summary>
    public static async Task<PostgresQualityRunStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresQualityRunStore(NpgsqlDataSource.Create(connectionString));
    }

    private static Guid Id(string id) => Guid.TryParseExact(id, "N", out var g) ? g : Guid.Empty;

    private const string RunColumns = "id, started_by, started_at, finished_at, status, repeat, total, done, summary, error";

    private static QualityRun ReadRun(NpgsqlDataReader r) => new QualityRun
    {
        Id = r.GetGuid(0).ToString("N"), StartedBy = r.GetString(1), StartedAt = r.GetFieldValue<DateTimeOffset>(2),
        FinishedAt = r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3), Status = r.GetString(4), Repeat = r.GetInt32(5), Total = r.GetInt32(6), Done = r.GetInt32(7),
        SummaryJson = r.IsDBNull(8) ? null : r.GetString(8), Error = r.IsDBNull(9) ? null : r.GetString(9),
    };

    /// <summary>
    /// Inserts a new quality run record.
    /// </summary>
    public async Task AddRunAsync(QualityRun run, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"insert into quality_runs (id, started_by, started_at, finished_at, status, repeat, total, done, summary, error)
              values (@id, @by, @started, @finished, @status, @repeat, @total, @done, @summary, @error)");
        Fill(command, run);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Updates an existing quality run record.
    /// </summary>
    public async Task UpdateRunAsync(QualityRun run, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"update quality_runs set started_by = @by, started_at = @started, finished_at = @finished, status = @status, repeat = @repeat, total = @total, done = @done, summary = @summary, error = @error where id = @id");
        Fill(command, run);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void Fill(NpgsqlCommand command, QualityRun run)
    {
        command.Parameters.AddWithValue("id", Id(run.Id));
        command.Parameters.AddWithValue("by", run.StartedBy);
        command.Parameters.AddWithValue("started", run.StartedAt);
        command.Parameters.AddWithValue("finished", (object?)run.FinishedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("status", run.Status);
        command.Parameters.AddWithValue("repeat", run.Repeat);
        command.Parameters.AddWithValue("total", run.Total);
        command.Parameters.AddWithValue("done", run.Done);
        command.Parameters.AddWithValue("summary", (object?)run.SummaryJson ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)run.Error ?? DBNull.Value);
    }

    /// <summary>
    /// Returns a quality run by id, or null when it does not exist.
    /// </summary>
    public async Task<QualityRun?> GetRunAsync(string id, CancellationToken ct = default)
    {
        var guid = Id(id);
        if (guid == Guid.Empty) { return null; }
        await using var command = _db.CreateCommand($"select {RunColumns} from quality_runs where id = @id");
        command.Parameters.AddWithValue("id", guid);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRun(reader) : null;
    }

    /// <summary>
    /// Lists the most recent quality runs, newest first.
    /// </summary>
    public async Task<IReadOnlyList<QualityRun>> ListRunsAsync(int take, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand($"select {RunColumns} from quality_runs order by started_at desc, id limit @take");
        command.Parameters.AddWithValue("take", take);
        var list = new List<QualityRun>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { list.Add(ReadRun(reader)); }
        return list;
    }

    /// <summary>
    /// Returns the most recent finished run before the given one, skipping unfinished runs.
    /// </summary>
    public async Task<QualityRun?> PreviousDoneRunAsync(QualityRun run, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand($"select {RunColumns} from quality_runs where status = 'done' and started_at < @at and id <> @id order by started_at desc limit 1");
        command.Parameters.AddWithValue("at", run.StartedAt);
        command.Parameters.AddWithValue("id", Id(run.Id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRun(reader) : null;
    }

    /// <summary>
    /// Inserts the result of one case within a quality run.
    /// </summary>
    public async Task AddResultAsync(QualityResult result, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"insert into quality_results (run_id, case_id, question, entity, verified, verdict, passes, runs, detail, duration_ms, reference_rows, fingerprint, data_changed)
              values (@run, @case, @question, @entity, @verified, @verdict, @passes, @runs, @detail, @ms, @rows, @fp, @changed)");
        command.Parameters.AddWithValue("run", Id(result.RunId));
        command.Parameters.AddWithValue("case", result.CaseId);
        command.Parameters.AddWithValue("question", result.Question);
        command.Parameters.AddWithValue("entity", result.Entity);
        command.Parameters.AddWithValue("verified", result.Verified);
        command.Parameters.AddWithValue("verdict", result.Verdict);
        command.Parameters.AddWithValue("passes", result.Passes);
        command.Parameters.AddWithValue("runs", result.Runs);
        command.Parameters.AddWithValue("detail", result.Detail);
        command.Parameters.AddWithValue("ms", result.DurationMs);
        command.Parameters.AddWithValue("rows", (object?)result.ReferenceRows ?? DBNull.Value);
        command.Parameters.AddWithValue("fp", (object?)result.Fingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("changed", result.DataChanged);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Lists the stored case results of a run.
    /// </summary>
    public async Task<IReadOnlyList<QualityResult>> ListResultsAsync(string runId, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"select run_id, case_id, question, entity, verified, verdict, passes, runs, detail, duration_ms, reference_rows, fingerprint, data_changed
              from quality_results where run_id = @run order by entity, question, case_id");
        command.Parameters.AddWithValue("run", Id(runId));
        var list = new List<QualityResult>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new QualityResult
            {
                RunId = reader.GetGuid(0).ToString("N"), CaseId = reader.GetString(1), Question = reader.GetString(2), Entity = reader.GetString(3), Verified = reader.GetBoolean(4),
                Verdict = reader.GetString(5), Passes = reader.GetInt32(6), Runs = reader.GetInt32(7), Detail = reader.GetString(8), DurationMs = reader.GetInt32(9),
                ReferenceRows = reader.IsDBNull(10) ? null : reader.GetInt32(10), Fingerprint = reader.IsDBNull(11) ? null : reader.GetString(11), DataChanged = reader.GetBoolean(12),
            });
        }
        return list;
    }

    /// <summary>
    /// Marks every unfinished run as failed with the given reason and returns how many were changed.
    /// </summary>
    public async Task<int> FailActiveAsync(string reason, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("update quality_runs set status = 'failed', error = @reason, finished_at = now() where status in ('queued', 'running')");
        command.Parameters.AddWithValue("reason", reason);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deletes finished runs older than the cutoff together with their results and returns the count.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from quality_runs where status not in ('queued', 'running') and coalesce(finished_at, started_at) < @cutoff");
        command.Parameters.AddWithValue("cutoff", cutoff);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
