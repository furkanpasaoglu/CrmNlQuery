using Npgsql;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Observability;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Observability;

/// <summary>
/// Live tests of the PostgreSQL suggestion counters against a real database, undoing what they add.
/// </summary>
[Trait("Category", "Live")]
public class PostgresSuggestionStatsTests
{
    /// <summary>
    /// Verifies that counters accumulate and the summary reads them.
    /// </summary>
    [Fact]
    public async Task CountersAccumulate_AndSummaryReadsThem()
    {
        var stats = await PostgresSuggestionStats.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var before = (await stats.SummaryAsync(1)).FirstOrDefault(s => s.Kind == "sort");
        try
        {
            await stats.AddShownAsync(new[] { "sort", "sort", "bogus" });
            await stats.AddClickAsync("sort");

            var after = (await stats.SummaryAsync(1)).Single(s => s.Kind == "sort");
            Assert.Equal((before?.Shown ?? 0) + 2, after.Shown);
            Assert.Equal((before?.Clicked ?? 0) + 1, after.Clicked);
        }
        finally
        {
            await using var db = NpgsqlDataSource.Create(PostgresHistoryStoreTests.ConnectionString);
            await using var undo = db.CreateCommand("update suggestion_stats set shown = shown - 2, clicked = clicked - 1 where kind = 'sort' and day = (now() at time zone 'utc')::date");
            await undo.ExecuteNonQueryAsync();
        }
    }
}
