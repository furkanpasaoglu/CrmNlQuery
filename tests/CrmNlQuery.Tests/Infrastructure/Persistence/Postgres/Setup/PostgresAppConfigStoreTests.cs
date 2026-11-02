using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Setup;
using CrmNlQuery.Tests.Contracts.Setup;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Setup;

/// <summary>
/// Tests the PostgreSQL setup settings store against a real database with the same contract as the memory store, and the synchronous startup reader.
/// </summary>
[Trait("Category", "Live")]
public class PostgresAppConfigStoreTests : AppConfigStoreContract
{
    protected override IAppConfigStore CreateStore()
    {
        return PostgresAppConfigStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task TheStartupReaderSeesWhatTheStoreWrote()
    {
        var store = CreateStore();
        await store.DeleteAllAsync();
        await store.SetManyAsync(new[] { new CrmNlQuery.Application.Setup.Models.AppConfigEntry("Crm:BaseUrl", "https://x.example", false) });

        var read = PostgresAppConfigReader.Read(PostgresHistoryStoreTests.ConnectionString);
        await store.DeleteAllAsync();

        Assert.Equal("https://x.example", Assert.Single(read).Value);
    }

    [Fact]
    public void TheStartupReaderReturnsNothingWhenTheDatabaseCannotBeReached()
    {
        Assert.Empty(PostgresAppConfigReader.Read("Host=127.0.0.1;Port=1;Username=x;Database=x;Timeout=1"));
        Assert.Empty(PostgresAppConfigReader.Read(null));
    }
}
