using CrmNlQuery.Tests.Contracts.Observability;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Observability;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Observability;

/// <summary>
/// Live PostgreSQL tests that run the shared report stats contract against the real database.
/// </summary>
[Trait("Category", "Live")]
public class PostgresReportStatsTests : ReportStatsContract
{
    protected override IReportStats CreateStats()
    {
        return PostgresReportStats.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }
}
