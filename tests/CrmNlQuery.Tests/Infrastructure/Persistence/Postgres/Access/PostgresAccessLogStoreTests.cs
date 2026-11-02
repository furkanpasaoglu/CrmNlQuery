using CrmNlQuery.Tests.Contracts.Access;
using Npgsql;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Access;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;
using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Tests the PostgreSQL access log store against a real database with the same contract as the in-memory store.
/// </summary>
[Trait("Category", "Live")]
public class PostgresAccessLogStoreTests : AccessLogStoreContract
{
    protected override IAccessLogStore CreateStore()
        => PostgresAccessLogStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();

    private static async Task<int> RawAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresHistoryStoreTests.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Verifies that the database refuses an update an AD hoc delete and a truncate.
    /// </summary>
    [Fact]
    public async Task TheDatabase_RefusesAnUpdate_AnAdHocDelete_AndATruncate()
    {
        var store = CreateStore();
        var user = Guid.NewGuid();
        var record = new DataAccessRecord(Guid.NewGuid(), Old, user, "ask", new[] { "account" }, 3, "ok", Guid.NewGuid());
        await store.AddAsync(record);
        try
        {
            var update = await Assert.ThrowsAsync<PostgresException>(() => RawAsync($"update data_access_log set rows = 999 where id = '{record.Id}'"));
            Assert.Contains("append-only", update.MessageText);
            var delete = await Assert.ThrowsAsync<PostgresException>(() => RawAsync($"delete from data_access_log where id = '{record.Id}'"));
            Assert.Contains("retention purge", delete.MessageText);
            await Assert.ThrowsAsync<PostgresException>(() => RawAsync("truncate data_access_log"));

            var found = await store.QueryAsync(new DataAccessQuery(Old.AddDays(-1), Old.AddDays(1), user, null, 10));
            Assert.Equal(3, Assert.Single(found).Rows);
        }
        finally
        {
            await store.PurgeOlderThanAsync(Cutoff);
        }
    }
}
