using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Tests.Contracts.Access;

/// <summary>
/// Shared behaviours every access log store implementation must pass without touching real records.
/// </summary>
public abstract class AccessLogStoreContract
{
    protected abstract IAccessLogStore CreateStore();

    protected static readonly DateTimeOffset Old = new DateTimeOffset(1901, 3, 1, 10, 0, 0, TimeSpan.Zero);
    protected static readonly DateTimeOffset Cutoff = new DateTimeOffset(1950, 1, 1, 0, 0, 0, TimeSpan.Zero);

    protected static DataAccessRecord Record(Guid user, DateTimeOffset at, string source = "ask", string tables = "account", int rows = 5, string outcome = "ok", Guid? request = null)
        => new DataAccessRecord(Guid.NewGuid(), at, user, source, tables.Split(','), rows, outcome, request ?? Guid.NewGuid());

    private static DataAccessQuery Window(Guid? user = null, string? table = null, int take = 100)
        => new DataAccessQuery(Old.AddDays(-1), Old.AddDays(30), user, table, take);

    /// <summary>
    /// Verifies that added records are returned newest first with all fields.
    /// </summary>
    [Fact]
    public async Task AddedRecords_AreReturned_NewestFirst_WithAllTheirFields()
    {
        var store = CreateStore();
        var user = Guid.NewGuid();
        var request = Guid.NewGuid();
        var first = Record(user, Old, "ask", "contract,account", 7, "ok", request);
        var second = Record(user, Old.AddMinutes(5), "lookup", "account", 2, "ok", request);
        await store.AddAsync(first);
        await store.AddAsync(second);
        try
        {
            var found = await store.QueryAsync(Window(user));

            Assert.Equal(new[] { second.Id, first.Id }, found.Select(r => r.Id));
            var back = found[1];
            Assert.Equal(first.At, back.At);
            Assert.Equal(user, back.User);
            Assert.Equal("ask", back.Source);
            Assert.Equal(new[] { "contract", "account" }, back.Tables);
            Assert.Equal(7, back.Rows);
            Assert.Equal("ok", back.Outcome);
            Assert.Equal(request, back.RequestId);
        }
        finally
        {
            await store.PurgeOlderThanAsync(Cutoff);
        }
    }

    /// <summary>
    /// Verifies that the query filters by user table and time range and honours take.
    /// </summary>
    [Fact]
    public async Task TheQuery_FiltersByUser_Table_AndTimeRange_AndHonoursTake()
    {
        var store = CreateStore();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await store.AddAsync(Record(a, Old, tables: "account"));
        await store.AddAsync(Record(a, Old.AddMinutes(1), tables: "contract,account"));
        await store.AddAsync(Record(b, Old.AddMinutes(2), tables: "contract"));
        await store.AddAsync(Record(a, Old.AddDays(60), tables: "account"));
        try
        {
            Assert.Equal(2, (await store.QueryAsync(Window(a))).Count);
            Assert.Equal(2, (await store.QueryAsync(Window(table: "contract"))).Count(r => r.User == a || r.User == b));
            Assert.Single(await store.QueryAsync(Window(b, "contract")));
            Assert.Empty(await store.QueryAsync(Window(b, "account")));
            Assert.Single(await store.QueryAsync(Window(a, take: 1)));
        }
        finally
        {
            await store.PurgeOlderThanAsync(Cutoff);
        }
    }

    /// <summary>
    /// Verifies that purge removes only older records and reports the count.
    /// </summary>
    [Fact]
    public async Task Purge_RemovesOnlyOlderRecords_AndReportsTheCount()
    {
        var store = CreateStore();
        var user = Guid.NewGuid();
        await store.AddAsync(Record(user, Old));
        await store.AddAsync(Record(user, Old.AddMinutes(1)));
        var recent = Record(user, DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.AddAsync(recent);
        try
        {
            var removed = await store.PurgeOlderThanAsync(Cutoff);

            Assert.True(removed >= 2);
            Assert.Empty(await store.QueryAsync(Window(user)));
            Assert.Contains(await store.QueryAsync(new DataAccessQuery(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), user, null, 10)), r => r.Id == recent.Id);
        }
        finally
        {
            await store.PurgeOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        }
    }

    /// <summary>
    /// Verifies that the store interface offers no update or delete only add query and retention purge.
    /// </summary>
    [Fact]
    public void TheStoreInterface_OffersNoUpdateOrDelete_OnlyAddQueryAndRetentionPurge()
    {
        var methods = typeof(IAccessLogStore).GetMethods().Select(m => m.Name).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "AddAsync", "PurgeOlderThanAsync", "QueryAsync" }, methods);
    }
}
