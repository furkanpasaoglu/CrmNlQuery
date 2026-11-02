namespace CrmNlQuery.Application.Operations.Services;

/// <summary>
/// Holds the slot of an allowed question and must be disposed when the work ends, otherwise the concurrency counter leaks.
/// </summary>
public sealed class GateLease : IDisposable
{
    private Action? _release;

    internal GateLease(Action release)
    {
        _release = release;
    }

    /// <summary>
    /// Releases the concurrency slot held by this lease; calling it more than once is harmless because only the first call counts.
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
