using CrmNlQuery.Tests.Contracts.History;
using Npgsql;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres;
using CrmNlQuery.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

/// <summary>
/// Tests the PostgreSQL history store against a real database with the same contract as the in-memory store.
/// </summary>
[Trait("Category", "Live")]
public class PostgresHistoryStoreTests : HistoryStoreContract
{
    public static string ConnectionString =>
        (Environment.GetEnvironmentVariable("NLQUERY_PG") ?? "Host=127.0.0.1;Port=5432;Username=nlquery;Database=nlquery") + ";Pooling=false";

    protected override IHistoryStore CreateStore()
    {
        return PostgresHistoryStore.CreateAsync(ConnectionString).GetAwaiter().GetResult();
    }


    /// <summary>
    /// Verifies that purge keeps pinned conversations and drops emptied ones.
    /// </summary>
    [Fact]
    public async Task Purge_KeepsPinnedConversations_AndDropsEmptiedOnes()
    {
        var store = await PostgresHistoryStore.CreateAsync(ConnectionString);
        var owner = Guid.NewGuid();
        var ancient = new DateTimeOffset(1901, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pinned = await store.CreateConversationAsync(owner, "pinned");
        var plain = await store.CreateConversationAsync(owner, "plain");
        var pinnedEntry = await store.AddAsync(owner, "q", "product", "<fetch />", null, pinned);
        var plainEntry = await store.AddAsync(owner, "q", "product", "<fetch />", null, plain);
        await store.UpdateConversationAsync(owner, pinned, null, true);
        await using var db = NpgsqlDataSource.Create(ConnectionString);
        await using (var age = db.CreateCommand("update history set created_at = @at where owner = @o; update conversations set updated_at = @at where owner = @o"))
        {
            age.Parameters.AddWithValue("at", ancient);
            age.Parameters.AddWithValue("o", owner);
            await age.ExecuteNonQueryAsync();
        }

        await store.PurgeOlderThanAsync(new DateTimeOffset(1950, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.NotNull(await store.GetAsync(owner, pinnedEntry));
        Assert.NotNull(await store.GetConversationAsync(owner, pinned));
        Assert.Null(await store.GetAsync(owner, plainEntry));
        Assert.Null(await store.GetConversationAsync(owner, plain));
        await store.DeleteConversationAsync(owner, pinned);
    }

    /// <summary>
    /// Verifies that migrations can run twice and record the schema version.
    /// </summary>
    [Fact]
    public async Task Migrations_CanRunTwice_AndRecordTheSchemaVersion()
    {
        await PostgresSchema.MigrateAsync(ConnectionString);
        await PostgresSchema.MigrateAsync(ConnectionString);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select max(version) from schema_version", connection);
        var version = (int)(await command.ExecuteScalarAsync())!;
        Assert.Equal(PostgresSchema.LatestVersion, version);
    }

    /// <summary>
    /// Verifies that parameterized SQL treats injection attempts as plain text.
    /// </summary>
    [Fact]
    public async Task ParameterizedSql_TreatsInjectionAttemptsAsPlainText()
    {
        var store = await PostgresHistoryStore.CreateAsync(ConnectionString);
        var owner = Guid.NewGuid();
        var evil = "x'); DELETE FROM history; --";
        var id = await store.AddAsync(owner, evil, evil, evil);
        Assert.Equal(evil, (await store.GetAsync(owner, id))!.Question);
        var chat = await store.CreateConversationAsync(owner, evil);
        Assert.Equal(evil, (await store.GetConversationAsync(owner, chat))!.Title);
        Assert.Single((await store.ListConversationsAsync(owner, 10, null, evil, null)).Items);
        await store.DeleteConversationAsync(owner, chat);
    }
}
