namespace CrmNlQuery.Web.Security;

/// <summary>
/// Limits how many sign-in attempts one address may make per minute, so challenges and guesses cannot be flooded.
/// </summary>
public sealed class AuthRateLimiter
{
    private readonly int _perMinute;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new object();
    private readonly Dictionary<string, (DateTimeOffset WindowStart, int Count)> _windows = new Dictionary<string, (DateTimeOffset, int)>();

    public AuthRateLimiter(int perMinute, Func<DateTimeOffset>? clock = null)
    {
        _perMinute = perMinute;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Counts one attempt for the address and returns false once it has used up its attempts in the current minute.
    /// </summary>
    public bool Allow(string address)
    {
        var now = _clock();
        lock (_gate)
        {
            if (_windows.Count > 10000)
            {
                var stale = _windows.Where(w => now - w.Value.WindowStart > TimeSpan.FromMinutes(1)).Select(w => w.Key).ToList();
                foreach (var key in stale) { _windows.Remove(key); }
            }
            if (!_windows.TryGetValue(address, out var window) || now - window.WindowStart >= TimeSpan.FromMinutes(1))
            {
                _windows[address] = (now, 1);
                return true;
            }
            if (window.Count >= _perMinute)
            {
                return false;
            }
            _windows[address] = (window.WindowStart, window.Count + 1);
            return true;
        }
    }
}
