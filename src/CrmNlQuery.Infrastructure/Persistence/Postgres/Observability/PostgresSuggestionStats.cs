using Npgsql;
using CrmNlQuery.Application.Suggestions.Abstractions;
using CrmNlQuery.Application.Suggestions.Models;
using CrmNlQuery.Application.Suggestions.Services;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Observability;

/// <summary>
/// Stores suggestion counters in PostgreSQL per day and kind, with no question text, field or user.
/// </summary>
public sealed class PostgresSuggestionStats : ISuggestionStats
{
    private readonly NpgsqlDataSource _db;

    private PostgresSuggestionStats(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the store and ensures its connection is ready.
    /// </summary>
    public static async Task<PostgresSuggestionStats> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresSuggestionStats(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Adds shown counts for the given suggestion kinds.
    /// </summary>
    public async Task AddShownAsync(IReadOnlyList<string> kinds, CancellationToken ct = default)
    {
        foreach (var kind in kinds.Where(SuggestionKinds.IsKnown))
        {
            await Add(kind, 1, 0, ct);
        }
    }

    /// <summary>
    /// Adds a click for a suggestion kind.
    /// </summary>
    public async Task AddClickAsync(string kind, CancellationToken ct = default)
    {
        if (SuggestionKinds.IsKnown(kind))
        {
            await Add(kind, 0, 1, ct);
        }
    }

    private async Task Add(string kind, int shown, int clicked, CancellationToken ct)
    {
        await using var command = _db.CreateCommand(
            "insert into suggestion_stats (day, kind, shown, clicked) values ((now() at time zone 'utc')::date, @kind, @shown, @clicked) " +
            "on conflict (day, kind) do update set shown = suggestion_stats.shown + excluded.shown, clicked = suggestion_stats.clicked + excluded.clicked");
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("shown", shown);
        command.Parameters.AddWithValue("clicked", clicked);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Returns the suggestion counters for the last days.
    /// </summary>
    public async Task<IReadOnlyList<SuggestionStat>> SummaryAsync(int days, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "select kind, coalesce(sum(shown), 0)::int, coalesce(sum(clicked), 0)::int from suggestion_stats " +
            "where day >= (now() at time zone 'utc')::date - (@days - 1) group by kind");
        command.Parameters.AddWithValue("days", Math.Max(1, days));
        var list = new List<SuggestionStat>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new SuggestionStat(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
        }
        return list.OrderBy(s => SuggestionKinds.All.ToList().IndexOf(s.Kind)).ToList();
    }

    /// <summary>
    /// Deletes suggestion_stats rows older than the cutoff day from PostgreSQL and returns how many were removed.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from suggestion_stats where day < @cutoff");
        command.Parameters.AddWithValue("cutoff", DateOnly.FromDateTime(cutoff.UtcDateTime));
        return await command.ExecuteNonQueryAsync(ct);
    }
}
