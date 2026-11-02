using CrmNlQuery.Tests.Contracts.Observability;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Observability;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;
using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Observability;

/// <summary>
/// Live PostgreSQL tests that run the shared log store contract against the real database.
/// </summary>
[Trait("Category", "Live")]
public class PostgresLogStoreTests : LogStoreContract
{
    protected override ILogStore CreateStore()
    {
        return PostgresLogStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Verifies that parameterized SQL treats injection attempts as plain text.
    /// </summary>
    [Fact]
    public async Task ParameterizedSql_TreatsInjectionAttemptsAsPlainText()
    {
        var store = await PostgresLogStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var evil = "x'); DELETE FROM app_logs; -- " + Guid.NewGuid().ToString("N");
        await store.AddAsync(new[] { new LogEntry(new DateTimeOffset(1901, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevels.Warning, evil, evil, evil) });

        var found = await store.QueryAsync(new LogQuery(LogLevels.Verbose, evil, null, 10));

        Assert.Equal(evil, found.Single().Message);
    }
}
