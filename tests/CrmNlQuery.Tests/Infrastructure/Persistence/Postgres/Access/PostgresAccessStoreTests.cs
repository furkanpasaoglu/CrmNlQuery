using CrmNlQuery.Tests.Contracts.Access;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Access;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Live PostgreSQL tests that run the shared access store contract against the real database.
/// </summary>
[Trait("Category", "Live")]
public class PostgresAccessStoreTests : AccessStoreContract
{
    protected override IAccessStore CreateStore()
    {
        return PostgresAccessStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Verifies that parameterized SQL treats injection attempts as plain text.
    /// </summary>
    [Fact]
    public async Task ParameterizedSql_TreatsInjectionAttemptsAsPlainText()
    {
        var store = await PostgresAccessStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString);
        var evil = "x'); DELETE FROM admin_users; --";
        var account = @"lab\" + evil + Guid.NewGuid().ToString("N");
        await store.SaveUserAsync(new AccessUser(account, evil, true, DateTimeOffset.UtcNow, evil, new List<Guid>()));

        var read = await store.GetUserAsync(account);

        Assert.Equal(evil, read!.DisplayName);
        await store.DeleteUserAsync(account);
    }
}
