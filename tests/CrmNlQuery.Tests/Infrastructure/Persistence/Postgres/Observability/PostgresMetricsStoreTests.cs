using CrmNlQuery.Tests.Contracts.Observability;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Observability;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;
using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Observability;

/// <summary>
/// Runs the shared metrics store tests against a real PostgreSQL.
/// </summary>
[Trait("Category", "Live")]
public class PostgresMetricsStoreTests : MetricsStoreContract
{
    protected override IMetricsStore CreateStore()
    {
        return PostgresMetricsStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Verifies that parameterized SQL treats injection attempts as plain text.
    /// </summary>
    [Fact]
    public async Task ParameterizedSql_TreatsInjectionAttemptsAsPlainText()
    {
        var store = await PostgresMetricsStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var day = new DateTimeOffset(1949, 12, 30, 0, 0, 0, TimeSpan.Zero);
        var evil = "x'); DELETE FROM requests; --";
        await store.RecordAiCallAsync(new AiCallRecord(Guid.NewGuid(), day.AddHours(1), evil, evil, evil, 5, 1, 1, "error", evil));
        await store.RecordRequestAsync(new RequestRecord(Guid.NewGuid(), day.AddHours(1), null, 5, "failure", evil));

        var summary = await store.GetSummaryAsync(day, day.AddDays(1));

        Assert.Equal(evil, summary.Latency.Single().Purpose);
        Assert.Equal(evil, summary.FailuresByType.Single().Key);
    }
}
