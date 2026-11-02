using Npgsql;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Application.Reports.Services;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Observability;

/// <summary>
/// Stores report counters in PostgreSQL per day and report name, with no question text or user, using parameterized values.
/// </summary>
public sealed class PostgresReportStats : IReportStats
{
    private readonly NpgsqlDataSource _db;

    private PostgresReportStats(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the PostgreSQL report stats store and ensures its schema is ready.
    /// </summary>
    public static async Task<PostgresReportStats> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresReportStats(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Counts the given report names as shown today.
    /// </summary>
    public async Task AddShownAsync(IReadOnlyList<string> names, CancellationToken ct = default)
    {
        foreach (var raw in names)
        {
            var name = ReportStatName.Clean(raw);
            if (name is not null) { await Add(name, 1, 0, ct); }
        }
    }

    /// <summary>
    /// Counts a report name as opened today.
    /// </summary>
    public async Task AddOpenedAsync(string name, CancellationToken ct = default)
    {
        var clean = ReportStatName.Clean(name);
        if (clean is not null) { await Add(clean, 0, 1, ct); }
    }

    private async Task Add(string name, int shown, int opened, CancellationToken ct)
    {
        await using var command = _db.CreateCommand(
            "insert into report_stats (day, name, shown, opened) values ((now() at time zone 'utc')::date, @name, @shown, @opened) " +
            "on conflict (day, name) do update set shown = report_stats.shown + excluded.shown, opened = report_stats.opened + excluded.opened");
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("shown", shown);
        command.Parameters.AddWithValue("opened", opened);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Returns the top reports by shown and opened counts over the last days.
    /// </summary>
    public async Task<IReadOnlyList<ReportStat>> SummaryAsync(int days, int top, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "select name, coalesce(sum(shown), 0)::int, coalesce(sum(opened), 0)::int from report_stats " +
            "where day >= (now() at time zone 'utc')::date - (@days - 1) group by name " +
            "order by sum(opened) desc, sum(shown) desc, name limit @top");
        command.Parameters.AddWithValue("days", Math.Max(1, days));
        command.Parameters.AddWithValue("top", Math.Max(1, top));
        var list = new List<ReportStat>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { list.Add(new ReportStat(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2))); }
        return list;
    }

    /// <summary>
    /// Deletes counters older than the cutoff and returns the count removed.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from report_stats where day < @cutoff");
        command.Parameters.AddWithValue("cutoff", DateOnly.FromDateTime(cutoff.UtcDateTime));
        return await command.ExecuteNonQueryAsync(ct);
    }
}
