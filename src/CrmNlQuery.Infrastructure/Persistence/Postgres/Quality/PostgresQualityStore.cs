using Npgsql;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Quality;

/// <summary>
/// Stores the acceptance-set cases in PostgreSQL; only questions, reference queries and rules are kept, never row data.
/// </summary>
public sealed class PostgresQualityStore : IQualityStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresQualityStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    public bool Persistent => true;

    /// <summary>
    /// Creates the store and ensures its connection is ready.
    /// </summary>
    public static async Task<PostgresQualityStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresQualityStore(NpgsqlDataSource.Create(connectionString));
    }

    private static bool TryId(string id, out Guid guid)
    {
        guid = Guid.Empty;
        return id is { Length: 32 } && Guid.TryParseExact(id, "N", out guid);
    }

    private static Guid[] Guids(IReadOnlyCollection<string> ids) => ids.Select(i => TryId(i, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToArray();

    private const string Columns = "id, created_by, created_at, source, status, verified, language, question, entity, answers, reference_fetch, compare, template, note";

    private static QualityCase Read(NpgsqlDataReader r) => new QualityCase
    {
        Id = r.GetGuid(0).ToString("N"), CreatedBy = r.GetString(1), CreatedAt = r.GetFieldValue<DateTimeOffset>(2), Source = r.GetString(3), Status = r.GetString(4),
        Verified = r.GetBoolean(5), Language = r.GetString(6), Question = r.GetString(7), Entity = r.GetString(8), AnswersJson = r.IsDBNull(9) ? null : r.GetString(9),
        ReferenceFetch = r.GetString(10), CompareJson = r.IsDBNull(11) ? null : r.GetString(11), Template = r.IsDBNull(12) ? null : r.GetString(12), Note = r.IsDBNull(13) ? null : r.GetString(13),
    };

    /// <summary>
    /// Inserts an acceptance-set case into the PostgreSQL quality_cases table with its question, expected answer and status.
    /// </summary>
    public async Task AddAsync(QualityCase item, CancellationToken ct = default)
    {
        TryId(item.Id, out var id);
        await using var command = _db.CreateCommand(
            @"insert into quality_cases (id, created_by, created_at, source, status, verified, language, question, entity, answers, reference_fetch, compare, template, note)
              values (@id, @by, @at, @source, @status, @verified, @lang, @question, @entity, @answers, @fetch, @compare, @template, @note)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("by", item.CreatedBy);
        command.Parameters.AddWithValue("at", item.CreatedAt);
        command.Parameters.AddWithValue("source", item.Source);
        command.Parameters.AddWithValue("status", item.Status);
        command.Parameters.AddWithValue("verified", item.Verified);
        command.Parameters.AddWithValue("lang", item.Language);
        command.Parameters.AddWithValue("question", item.Question);
        command.Parameters.AddWithValue("entity", item.Entity);
        command.Parameters.AddWithValue("answers", (object?)item.AnswersJson ?? DBNull.Value);
        command.Parameters.AddWithValue("fetch", item.ReferenceFetch);
        command.Parameters.AddWithValue("compare", (object?)item.CompareJson ?? DBNull.Value);
        command.Parameters.AddWithValue("template", (object?)item.Template ?? DBNull.Value);
        command.Parameters.AddWithValue("note", (object?)item.Note ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Reads acceptance-set cases from PostgreSQL matching the optional status, source, verified and entity filters.
    /// </summary>
    public async Task<IReadOnlyList<QualityCase>> ListAsync(QualityCaseFilter filter, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            $@"select {Columns} from quality_cases
               where (@status::text is null or status = @status) and (@source::text is null or source = @source)
                 and (@verified::boolean is null or verified = @verified) and (@entity::text is null or entity = @entity)
               order by entity, created_at, id");
        command.Parameters.AddWithValue("status", (object?)filter.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("source", (object?)filter.Source ?? DBNull.Value);
        command.Parameters.AddWithValue("verified", (object?)filter.Verified ?? DBNull.Value);
        command.Parameters.AddWithValue("entity", (object?)filter.Entity ?? DBNull.Value);
        var list = new List<QualityCase>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { list.Add(Read(reader)); }
        return list;
    }

    /// <summary>
    /// Returns a case by id, or null.
    /// </summary>
    public async Task<QualityCase?> GetAsync(string id, CancellationToken ct = default)
    {
        if (!TryId(id, out var guid)) { return null; }
        await using var command = _db.CreateCommand($"select {Columns} from quality_cases where id = @id");
        command.Parameters.AddWithValue("id", guid);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// Returns the number of rows in the PostgreSQL quality_cases table.
    /// </summary>
    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("select count(*) from quality_cases");
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Checks whether a case with the same question and reference query exists.
    /// </summary>
    public async Task<bool> ExistsAsync(string question, string referenceFetch, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("select 1 from quality_cases where question = @q and reference_fetch = @f limit 1");
        command.Parameters.AddWithValue("q", question);
        command.Parameters.AddWithValue("f", referenceFetch);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private async Task<int> ExecuteAsync(string sql, IReadOnlyCollection<string> ids, Action<NpgsqlCommand> extra, CancellationToken ct)
    {
        var guids = Guids(ids);
        if (guids.Length == 0) { return 0; }
        await using var command = _db.CreateCommand(sql);
        command.Parameters.AddWithValue("ids", guids);
        extra(command);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Sets the status of the given cases.
    /// </summary>
    public Task<int> SetStatusAsync(IReadOnlyCollection<string> ids, string status, CancellationToken ct = default)
        => ExecuteAsync("update quality_cases set status = @status where id = any(@ids)", ids, c => c.Parameters.AddWithValue("status", status), ct);

    /// <summary>
    /// Sets the verified flag of the given cases.
    /// </summary>
    public Task<int> SetVerifiedAsync(IReadOnlyCollection<string> ids, bool verified, CancellationToken ct = default)
        => ExecuteAsync("update quality_cases set verified = @verified where id = any(@ids)", ids, c => c.Parameters.AddWithValue("verified", verified), ct);

    /// <summary>
    /// Deletes the acceptance-set cases with the given ids from PostgreSQL and returns how many were removed.
    /// </summary>
    public Task<int> DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default)
        => ExecuteAsync("delete from quality_cases where id = any(@ids)", ids, _ => { }, ct);
}
