using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Setup;

/// <summary>
/// In-memory setup settings for tests; they are lost when the service restarts.
/// </summary>
public sealed class MemoryAppConfigStore : IAppConfigStore
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, AppConfigEntry> _items = new Dictionary<string, AppConfigEntry>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns every stored setting.
    /// </summary>
    public Task<IReadOnlyList<AppConfigEntry>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<AppConfigEntry>>(_items.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToList()); }
    }

    /// <summary>
    /// Adds or replaces the given settings.
    /// </summary>
    public Task SetManyAsync(IReadOnlyList<AppConfigEntry> entries, CancellationToken ct = default)
    {
        lock (_gate)
        {
            foreach (var entry in entries) { _items[entry.Key] = entry; }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes every stored setting.
    /// </summary>
    public Task DeleteAllAsync(CancellationToken ct = default)
    {
        lock (_gate) { _items.Clear(); }
        return Task.CompletedTask;
    }
}
