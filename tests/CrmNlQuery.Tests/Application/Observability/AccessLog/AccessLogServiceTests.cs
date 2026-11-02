using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Application.Observability.AccessLog;

/// <summary>
/// Tests that access log rows carry user names resolved from the CRM and still list when names cannot be resolved.
/// </summary>
public class AccessLogServiceTests
{
    private sealed class FakeNames : IUserNames
    {
        public Dictionary<Guid, string> Known = new Dictionary<Guid, string>();
        public bool Fail;
        public int Calls;
        public List<Guid> Asked = new List<Guid>();

        /// <summary>
        /// Returns canned user names for the given ids.
        /// </summary>
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            Calls++;
            Asked.AddRange(ids);
            if (Fail) { throw new InvalidOperationException("CRM down"); }
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(Known);
        }
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static DataAccessQuery Window => new DataAccessQuery(Now.AddDays(-1), Now.AddDays(1), null, null, 100);

    private static DataAccessRecord Record(Guid user, int minutes) =>
        new DataAccessRecord(Guid.NewGuid(), Now.AddMinutes(minutes), user, "ask", new[] { "account" }, 4, "ok", Guid.NewGuid());

    /// <summary>
    /// Verifies that rows carry the user name when the CRM knows it.
    /// </summary>
    [Fact]
    public async Task Rows_CarryTheUserName_WhenTheCrmKnowsIt()
    {
        var store = new MemoryAccessLogStore();
        var alice = Guid.NewGuid();
        await store.AddAsync(Record(alice, 1));
        var names = new FakeNames { Known = { [alice] = "Alice A." } };

        var rows = await new AccessLogService(store, names).ListAsync(Window);

        var row = Assert.Single(rows);
        Assert.Equal("Alice A.", row.UserName);
        Assert.Equal(alice, row.User);
        Assert.Equal(4, row.Rows);
        Assert.Equal(new[] { "account" }, row.Tables);
    }

    /// <summary>
    /// Verifies that an unknown user has no name but the row still comes.
    /// </summary>
    [Fact]
    public async Task AnUnknownUser_HasNoName_ButTheRowStillComes()
    {
        var store = new MemoryAccessLogStore();
        await store.AddAsync(Record(Guid.NewGuid(), 1));

        var rows = await new AccessLogService(store, new FakeNames()).ListAsync(Window);

        Assert.Null(Assert.Single(rows).UserName);
    }

    /// <summary>
    /// Verifies that when the CRM is down the list still comes without names.
    /// </summary>
    [Fact]
    public async Task WhenTheCrmIsDown_TheListStillComes_WithoutNames()
    {
        var store = new MemoryAccessLogStore();
        await store.AddAsync(Record(Guid.NewGuid(), 1));

        var rows = await new AccessLogService(store, new FakeNames { Fail = true }).ListAsync(Window);

        Assert.Null(Assert.Single(rows).UserName);
    }

    /// <summary>
    /// Verifies that each user is looked up once and an empty list asks nobody.
    /// </summary>
    [Fact]
    public async Task EachUserIsLookedUpOnce_AndAnEmptyListAsksNobody()
    {
        var store = new MemoryAccessLogStore();
        var a = Guid.NewGuid();
        await store.AddAsync(Record(a, 1));
        await store.AddAsync(Record(a, 2));
        var names = new FakeNames();

        await new AccessLogService(store, names).ListAsync(Window);
        Assert.Equal(new[] { a }, names.Asked);

        var empty = new FakeNames();
        await new AccessLogService(new MemoryAccessLogStore(), empty).ListAsync(Window);
        Assert.Equal(0, empty.Calls);
    }
}
