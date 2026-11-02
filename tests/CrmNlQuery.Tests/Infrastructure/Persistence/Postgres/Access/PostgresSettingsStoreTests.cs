using CrmNlQuery.Tests.Contracts.Access;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Access;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Tests the PostgreSQL settings store against a real database with the same contract as the in-memory store.
/// </summary>
[Trait("Category", "Live")]
public class PostgresSettingsStoreTests : SettingsStoreContract
{
    protected override ISettingsStore CreateStore()
    {
        return PostgresSettingsStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }
}
