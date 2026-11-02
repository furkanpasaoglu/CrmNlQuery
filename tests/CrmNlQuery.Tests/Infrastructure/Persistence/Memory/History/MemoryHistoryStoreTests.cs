using CrmNlQuery.Tests.Contracts.History;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.History;

/// <summary>
/// Runs the shared history store tests against the memory store.
/// </summary>
public class MemoryHistoryStoreTests : HistoryStoreContract
{
    protected override IHistoryStore CreateStore() => new MemoryHistoryStore();

    /// <summary>
    /// Verifies that when full the oldest entry is dropped.
    /// </summary>
    [Fact]
    public async Task WhenFull_TheOldestEntryIsDropped()
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var store = new MemoryHistoryStore(() => now, capacity: 2);
        var owner = Guid.NewGuid();
        var first = await store.AddAsync(owner, "1", "product", "<fetch />");
        now = now.AddSeconds(1);
        var second = await store.AddAsync(owner, "2", "product", "<fetch />");
        now = now.AddSeconds(1);
        var third = await store.AddAsync(owner, "3", "product", "<fetch />");

        Assert.Null(await store.GetAsync(owner, first));
        Assert.NotNull(await store.GetAsync(owner, second));
        Assert.NotNull(await store.GetAsync(owner, third));
    }


    /// <summary>
    /// Verifies that purge never touches aPinned conversation and drops emptied ones.
    /// </summary>
    [Fact]
    public async Task Purge_NeverTouchesAPinnedConversation_AndDropsEmptiedOnes()
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var store = new MemoryHistoryStore(() => now);
        var owner = Guid.NewGuid();
        var pinned = await store.CreateConversationAsync(owner, "pinned");
        var plain = await store.CreateConversationAsync(owner, "plain");
        var oldPinned = await store.AddAsync(owner, "old in pinned", "product", "<fetch />", null, pinned);
        var oldPlain = await store.AddAsync(owner, "old in plain", "product", "<fetch />", null, plain);
        await store.UpdateConversationAsync(owner, pinned, null, true);
        now = now.AddDays(100);

        var removed = await store.PurgeOlderThanAsync(now.AddDays(-90));

        Assert.Equal(1, removed);
        Assert.NotNull(await store.GetAsync(owner, oldPinned));
        Assert.NotNull(await store.GetConversationAsync(owner, pinned));
        Assert.Null(await store.GetAsync(owner, oldPlain));
        Assert.Null(await store.GetConversationAsync(owner, plain));
    }
}
