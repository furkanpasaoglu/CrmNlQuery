using CrmNlQuery.Application.Operations.Abstractions;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// In-memory settings store used when no database is configured and in tests; values are lost on restart.
/// </summary>
public sealed class MemorySettingsStore : ISettingsStore
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Returns a copy of all in-memory runtime setting overrides as a key-to-value dictionary.
    /// </summary>
    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_values, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Stores a runtime setting value in memory under the lock, ignoring the actor and time.
    /// </summary>
    public Task SetAsync(string key, string value, string actor, DateTimeOffset at, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _values[key] = value;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes a runtime setting from the in-memory store under the lock.
    /// </summary>
    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _values.Remove(key);
        }
        return Task.CompletedTask;
    }
}
