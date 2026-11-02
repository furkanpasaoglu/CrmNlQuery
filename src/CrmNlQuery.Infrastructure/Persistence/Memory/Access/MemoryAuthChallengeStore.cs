using CrmNlQuery.Application.Access.Abstractions;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// In-memory sign-in challenges for development and tests; they are lost when the service restarts.
/// </summary>
public sealed class MemoryAuthChallengeStore : IAuthChallengeStore
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, (string SecretHash, DateTimeOffset Expires, bool Used)> _items = new Dictionary<string, (string, DateTimeOffset, bool)>();

    /// <summary>
    /// Stores a new challenge by nonce hash.
    /// </summary>
    public Task AddAsync(string nonceHash, string secretHash, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _items[nonceHash] = (secretHash, expiresAt, false);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Uses the challenge once if the secret hash matches and it has not expired.
    /// </summary>
    public Task<bool> TryConsumeAsync(string nonceHash, string secretHash, DateTimeOffset now, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(nonceHash, out var item) || item.Used || item.Expires <= now || !string.Equals(item.SecretHash, secretHash, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }
            _items[nonceHash] = (item.SecretHash, item.Expires, true);
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Removes challenges that expired before the given time.
    /// </summary>
    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var old = _items.Where(i => i.Value.Expires < before).Select(i => i.Key).ToList();
            foreach (var key in old) { _items.Remove(key); }
            return Task.FromResult(old.Count);
        }
    }
}
