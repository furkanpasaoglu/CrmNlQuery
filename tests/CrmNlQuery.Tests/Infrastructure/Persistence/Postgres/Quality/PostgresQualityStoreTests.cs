using CrmNlQuery.Tests.Contracts.Quality;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Quality;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Quality;

/// <summary>
/// Runs the shared quality store tests against a real PostgreSQL.
/// </summary>
[Trait("Category", "Live")]
public class PostgresQualityStoreTests : QualityStoreContract
{
    protected override IQualityStore CreateStore()
    {
        return PostgresQualityStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }
}
