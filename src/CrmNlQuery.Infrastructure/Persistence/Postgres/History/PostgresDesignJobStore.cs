using Npgsql;
using CrmNlQuery.Application.Design.Abstractions;
using CrmNlQuery.Application.Design.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.History;

/// <summary>
/// Stores design jobs in PostgreSQL; the request text is visible only to its owner and values always go as parameters.
/// </summary>
public sealed class PostgresDesignJobStore : IDesignJobStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresDesignJobStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the design job store, applying the schema first.
    /// </summary>
    public static async Task<PostgresDesignJobStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresDesignJobStore(NpgsqlDataSource.Create(connectionString));
    }

    private static bool TryId(string id, out Guid guid)
    {
        guid = Guid.Empty;
        return id is { Length: 32 } && Guid.TryParseExact(id, "N", out guid);
    }

    private const string Columns = "id, owner, status, step, percent, title, prefix, request, error, problem_count, created_at, updated_at, finished_at";

    private static DesignJob Read(NpgsqlDataReader r, bool withResult) => new DesignJob
    {
        Id = r.GetGuid(0).ToString("N"), Owner = r.GetString(1), Status = r.GetString(2), Step = r.GetString(3), Percent = r.GetInt32(4), Title = r.GetString(5),
        Prefix = r.IsDBNull(6) ? null : r.GetString(6), Request = r.GetString(7), Error = r.IsDBNull(8) ? null : r.GetString(8), ProblemCount = r.GetInt32(9),
        CreatedAt = r.GetFieldValue<DateTimeOffset>(10), UpdatedAt = r.GetFieldValue<DateTimeOffset>(11), FinishedAt = r.IsDBNull(12) ? null : r.GetFieldValue<DateTimeOffset>(12),
        Result = withResult && !r.IsDBNull(13) ? r.GetString(13) : null,
    };

    /// <summary>
    /// Inserts a new design job row into the PostgreSQL design_jobs table with its owner, status, progress and request.
    /// </summary>
    public async Task AddAsync(DesignJob job, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"insert into design_jobs (id, owner, status, step, percent, title, prefix, request, result, error, problem_count, created_at, updated_at, finished_at)
              values (@id, @owner, @status, @step, @percent, @title, @prefix, @request, @result, @error, @problems, @created, @updated, @finished)");
        Fill(command, job);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Updates a design job's status, step, percent, result, error and timestamps in the PostgreSQL design_jobs table.
    /// </summary>
    public async Task UpdateAsync(DesignJob job, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"update design_jobs set status = @status, step = @step, percent = @percent, result = @result, error = @error, problem_count = @problems,
                     updated_at = @updated, finished_at = @finished where id = @id and owner = @owner");
        Fill(command, job);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void Fill(NpgsqlCommand command, DesignJob job)
    {
        command.Parameters.AddWithValue("id", Guid.ParseExact(job.Id, "N"));
        command.Parameters.AddWithValue("owner", job.Owner);
        command.Parameters.AddWithValue("status", job.Status);
        command.Parameters.AddWithValue("step", job.Step);
        command.Parameters.AddWithValue("percent", job.Percent);
        command.Parameters.AddWithValue("title", job.Title);
        command.Parameters.AddWithValue("prefix", job.Prefix is null ? DBNull.Value : job.Prefix);
        command.Parameters.AddWithValue("request", job.Request);
        command.Parameters.AddWithValue("result", job.Result is null ? DBNull.Value : job.Result);
        command.Parameters.AddWithValue("error", job.Error is null ? DBNull.Value : job.Error);
        command.Parameters.AddWithValue("problems", job.ProblemCount);
        command.Parameters.AddWithValue("created", job.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("updated", job.UpdatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("finished", job.FinishedAt is { } f ? f.ToUniversalTime() : DBNull.Value);
    }

    /// <summary>
    /// Returns the owner's design job by id, or null.
    /// </summary>
    public async Task<DesignJob?> GetAsync(string owner, string id, CancellationToken ct = default)
    {
        if (!TryId(id, out var guid))
        {
            return null;
        }
        await using var command = _db.CreateCommand($"select {Columns}, result from design_jobs where id = @id and lower(owner) = lower(@owner)");
        command.Parameters.AddWithValue("id", guid);
        command.Parameters.AddWithValue("owner", owner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, true) : null;
    }

    /// <summary>
    /// Lists the owner's design jobs, newest first up to the given count.
    /// </summary>
    public async Task<IReadOnlyList<DesignJob>> ListAsync(string owner, int take, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand($"select {Columns} from design_jobs where lower(owner) = lower(@owner) order by created_at desc limit @take");
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("take", Math.Max(1, take));
        var list = new List<DesignJob>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(Read(reader, false));
        }
        return list;
    }

    /// <summary>
    /// Deletes the owner's design job, returning whether one was deleted.
    /// </summary>
    public async Task<bool> DeleteAsync(string owner, string id, CancellationToken ct = default)
    {
        if (!TryId(id, out var guid))
        {
            return false;
        }
        await using var command = _db.CreateCommand("delete from design_jobs where id = @id and lower(owner) = lower(@owner)");
        command.Parameters.AddWithValue("id", guid);
        command.Parameters.AddWithValue("owner", owner);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Marks all queued or running jobs as failed with the given reason, used after a restart.
    /// </summary>
    public async Task<int> FailActiveAsync(string reason, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "update design_jobs set status = 'failed', error = @reason, step = 'Failed', finished_at = now(), updated_at = now() where status in ('queued', 'running')");
        command.Parameters.AddWithValue("reason", reason);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deletes design jobs older than the cutoff.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from design_jobs where created_at < @cutoff and status not in ('queued', 'running')");
        command.Parameters.AddWithValue("cutoff", cutoff.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(ct);
    }
}
