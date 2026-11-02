using CrmNlQuery.Application.Preferences.Abstractions;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// In-memory preferences store used when no database is configured and in tests.
/// </summary>
public sealed class MemoryPreferencesStore : IPreferencesStore
{
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, string> _names = new Dictionary<Guid, string>();

    /// <summary>
    /// Reads the owner's saved display name from memory, or null.
    /// </summary>
    public Task<string?> GetDisplayNameAsync(Guid owner, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_names.TryGetValue(owner, out var name) ? name : null);
        }
    }

    private readonly Dictionary<string, string> _languages = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Reads the account's saved interface language from memory, or null.
    /// </summary>
    public Task<string?> GetLanguageAsync(string account, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_languages.TryGetValue(account, out var language) ? language : null);
        }
    }

    /// <summary>
    /// Saves or clears the account's interface language in memory.
    /// </summary>
    public Task SetLanguageAsync(string account, string? language, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(language))
            {
                _languages.Remove(account);
            }
            else
            {
                _languages[account] = language;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Saves or clears the owner's display name in memory.
    /// </summary>
    public Task SetDisplayNameAsync(Guid owner, string? displayName, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                _names.Remove(owner);
            }
            else
            {
                _names[owner] = displayName;
            }
        }
        return Task.CompletedTask;
    }
}
