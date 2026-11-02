using CrmNlQuery.Tests.Contracts.Observability;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Observability;
using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Observability;

/// <summary>
/// Tests the in-memory log store against the shared log store contract.
/// </summary>
public class MemoryLogStoreTests : LogStoreContract
{
    protected override ILogStore CreateStore() => new MemoryLogStore();

    /// <summary>
    /// Verifies that the memory store keeps only the newest entries when its capacity is exceeded.
    /// </summary>
    [Fact]
    public async Task TheMemoryStore_KeepsOnlyTheNewestEntries_WhenItsCapacityIsExceeded()
    {
        var store = new MemoryLogStore(capacity: 3);
        var start = DateTimeOffset.UtcNow;
        await store.AddAsync(Enumerable.Range(0, 5).Select(i => new LogEntry(start.AddSeconds(i), LogLevels.Warning, "n" + i, null, null)).ToList());

        var kept = await store.QueryAsync(new LogQuery(LogLevels.Verbose, null, null, 100));

        Assert.Equal(new[] { "n4", "n3", "n2" }, kept.Select(e => e.Message));
    }
}
