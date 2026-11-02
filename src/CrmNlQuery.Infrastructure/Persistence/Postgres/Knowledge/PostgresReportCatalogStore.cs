using System.Text.Json;
using Npgsql;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Domain.Reports.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Knowledge;

/// <summary>
/// Stores the report catalog in PostgreSQL, keeping keywords as a JSON array and always passing values as parameters.
/// </summary>
public sealed class PostgresReportCatalogStore : IReportCatalogStore
{
    private const string Select = "select id, name, description, keywords, crm_report_id, url, record_entity, record_param, enabled, updated_by, updated_at from report_catalog";
    private readonly NpgsqlDataSource _db;

    private PostgresReportCatalogStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the PostgreSQL report catalog store and ensures its schema is ready.
    /// </summary>
    public static async Task<PostgresReportCatalogStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresReportCatalogStore(NpgsqlDataSource.Create(connectionString));
    }

    public bool Persistent => true;

    private static ReportCatalogEntry Read(NpgsqlDataReader r) => new ReportCatalogEntry
    {
        Id = r.GetString(0),
        Name = r.GetString(1),
        Description = r.IsDBNull(2) ? null : r.GetString(2),
        Keywords = JsonSerializer.Deserialize<List<string>>(r.GetString(3)) ?? new List<string>(),
        CrmReportId = r.IsDBNull(4) ? null : r.GetString(4),
        Url = r.IsDBNull(5) ? null : r.GetString(5),
        RecordEntity = r.IsDBNull(6) ? null : r.GetString(6),
        RecordParam = r.IsDBNull(7) ? null : r.GetString(7),
        Enabled = r.GetBoolean(8),
        UpdatedBy = r.GetString(9),
        UpdatedAt = r.GetFieldValue<DateTimeOffset>(10),
    };

    /// <summary>
    /// Reads all report catalog entries from PostgreSQL, ordered by lowercase name then id.
    /// </summary>
    public async Task<IReadOnlyList<ReportCatalogEntry>> ListAsync(CancellationToken ct = default)
    {
        var list = new List<ReportCatalogEntry>();
        await using var command = _db.CreateCommand(Select + " order by lower(name), id");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { list.Add(Read(reader)); }
        return list;
    }

    /// <summary>
    /// Returns a report catalog entry by id, or null.
    /// </summary>
    public async Task<ReportCatalogEntry?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(Select + " where id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// Inserts or updates a report catalog entry.
    /// </summary>
    public async Task UpsertAsync(ReportCatalogEntry entry, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "insert into report_catalog (id, name, description, keywords, crm_report_id, url, record_entity, record_param, enabled, updated_by, updated_at) "
            + "values (@id, @name, @description, @keywords, @crm, @url, @entity, @param, @enabled, @by, @at) "
            + "on conflict (id) do update set name = @name, description = @description, keywords = @keywords, crm_report_id = @crm, url = @url, "
            + "record_entity = @entity, record_param = @param, enabled = @enabled, updated_by = @by, updated_at = @at");
        command.Parameters.AddWithValue("id", entry.Id);
        command.Parameters.AddWithValue("name", entry.Name);
        command.Parameters.AddWithValue("description", (object?)entry.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("keywords", JsonSerializer.Serialize(entry.Keywords));
        command.Parameters.AddWithValue("crm", (object?)entry.CrmReportId ?? DBNull.Value);
        command.Parameters.AddWithValue("url", (object?)entry.Url ?? DBNull.Value);
        command.Parameters.AddWithValue("entity", (object?)entry.RecordEntity ?? DBNull.Value);
        command.Parameters.AddWithValue("param", (object?)entry.RecordParam ?? DBNull.Value);
        command.Parameters.AddWithValue("enabled", entry.Enabled);
        command.Parameters.AddWithValue("by", entry.UpdatedBy);
        command.Parameters.AddWithValue("at", entry.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deletes a report catalog entry and returns whether it existed.
    /// </summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from report_catalog where id = @id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }
}
