using CrmNlQuery.Tests.Contracts.Knowledge;
using CrmNlQuery.Application.Glossary.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Knowledge;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Knowledge;

/// <summary>
/// Runs the glossary store contract tests against a real PostgreSQL, the same contract as the in-memory store.
/// </summary>
[Trait("Category", "Live")]
public class PostgresGlossaryStoreTests : GlossaryStoreContract
{
    protected override IGlossaryStore CreateStore()
    {
        return PostgresGlossaryStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }
}
