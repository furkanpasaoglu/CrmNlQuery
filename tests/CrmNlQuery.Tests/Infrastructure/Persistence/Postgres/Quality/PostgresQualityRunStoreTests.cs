using CrmNlQuery.Tests.Contracts.Quality;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Quality;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Quality;

/// <summary>
/// Tests the PostgreSQL quality run store against a real database with the same contract as the in-memory store.
/// </summary>
[Trait("Category", "Live")]
public class PostgresQualityRunStoreTests : QualityRunStoreContract
{
    protected override IQualityRunStore CreateStore()
    {
        return PostgresQualityRunStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }
}
