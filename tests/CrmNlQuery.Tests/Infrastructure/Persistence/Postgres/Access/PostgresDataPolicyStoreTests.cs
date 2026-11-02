using CrmNlQuery.Tests.Contracts.Access;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Access;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Live tests of the PostgreSQL data policy store against a real database, passing the same contract as the memory store.
/// </summary>
[Trait("Category", "Live")]
public class PostgresDataPolicyStoreTests : DataPolicyStoreContract
{
    protected override IDataPolicyStore CreateStore()
    {
        return PostgresDataPolicyStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }
}
