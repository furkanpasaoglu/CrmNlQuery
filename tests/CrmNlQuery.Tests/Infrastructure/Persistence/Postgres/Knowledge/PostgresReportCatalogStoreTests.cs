using CrmNlQuery.Tests.Contracts.Knowledge;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Knowledge;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Knowledge;

/// <summary>
/// Live PostgreSQL tests that run the shared report catalog store contract against the real database.
/// </summary>
[Trait("Category", "Live")]
public class PostgresReportCatalogStoreTests : ReportCatalogStoreContract
{
    protected override IReportCatalogStore CreateStore()
    {
        return PostgresReportCatalogStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }
}
