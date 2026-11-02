using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Application.Access.Services;

/// <summary>
/// Resolves a CRM user from a Windows account with a short cache that stores only users that were found.
/// </summary>
public sealed class CachingCallerResolver : ICallerResolver
{
    private readonly ICallerResolver _inner;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _timeToLive;
    private readonly object _gate = new object();
    private readonly Dictionary<string, (Guid Id, DateTimeOffset Expires)> _cache = new Dictionary<string, (Guid, DateTimeOffset)>();

    public CachingCallerResolver(ICallerResolver inner, Func<DateTimeOffset> clock, TimeSpan timeToLive)
    {
        _inner = inner;
        _clock = clock;
        _timeToLive = timeToLive;
    }

    /// <summary>
    /// Resolves the CRM user id for a Windows account, using the cache first.
    /// </summary>
    public async Task<Guid?> ResolveAsync(string windowsAccount, CancellationToken ct = default)
    {
        var key = NameNormalizer.Fold(windowsAccount.Trim());
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit) && hit.Expires > _clock())
            {
                return hit.Id;
            }
        }

        var found = await _inner.ResolveAsync(windowsAccount, ct);
        if (found is not null)
        {
            lock (_gate)
            {
                _cache[key] = (found.Value, _clock() + _timeToLive);
            }
        }
        return found;
    }
}
