using CrmNlQuery.Tests.Contracts.Access;
using Npgsql;
using CrmNlQuery.Application.Preferences.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Access;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Tests the PostgreSQL preferences store against a real database with the same contract as the memory store.
/// </summary>
[Trait("Category", "Live")]
public class PostgresPreferencesStoreTests : PreferencesStoreContract
{
    protected override IPreferencesStore CreateStore()
    {
        return PostgresPreferencesStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Verifies that migrations create the table and an empty name is not stored as a row.
    /// </summary>
    [Fact]
    public async Task Migrations_CreateTheTable_AndAnEmptyNameIsNotStoredAsARow()
    {
        var store = await PostgresPreferencesStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var me = Guid.NewGuid();
        await store.SetDisplayNameAsync(me, "x");
        await store.SetDisplayNameAsync(me, null);

        await using var db = NpgsqlDataSource.Create(PostgresHistoryStoreTests.ConnectionString);
        await using var command = db.CreateCommand("select count(*) from user_preferences where owner = @o");
        command.Parameters.AddWithValue("o", me);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }
}
