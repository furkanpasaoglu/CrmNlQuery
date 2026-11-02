using CrmNlQuery.Application.Observability.Abstractions;

namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Deletes old metrics records, with a default of 90 days; zero or negative days deletes nothing.
/// </summary>
public static class MetricsRetention
{
    /// <summary>
    /// Purges metrics older than the given number of days from the store once, using the clock.
    /// </summary>
    public static Task<int> PurgeOnceAsync(IMetricsStore store, int days, Func<DateTimeOffset> clock, CancellationToken ct = default)
        => PurgeOnceAsync((cutoff, token) => store.PurgeOlderThanAsync(cutoff, token), days, clock, ct);

    /// <summary>
    /// Purges once through a delegate, deleting nothing when the number of days is zero or negative.
    /// </summary>
    public static async Task<int> PurgeOnceAsync(Func<DateTimeOffset, CancellationToken, Task<int>> purge, int days, Func<DateTimeOffset> clock, CancellationToken ct = default)
    {
        if (days <= 0)
        {
            return 0;
        }
        return await purge(clock().AddDays(-days), ct);
    }

    /// <summary>
    /// Runs the metrics purge at startup and then daily using a fixed number of days.
    /// </summary>
    public static Task RunAsync(IMetricsStore store, int days, Action<Exception> onError, CancellationToken ct)
        => RunAsync((cutoff, token) => store.PurgeOlderThanAsync(cutoff, token), () => days, onError, ct);

    /// <summary>
    /// Runs the metrics purge at startup and then daily, reading the number of days each round.
    /// </summary>
    public static Task RunAsync(IMetricsStore store, Func<int> days, Action<Exception> onError, CancellationToken ct)
        => RunAsync((cutoff, token) => store.PurgeOlderThanAsync(cutoff, token), days, onError, ct);

    /// <summary>
    /// Runs a purge delegate at startup and then daily with a fixed number of days.
    /// </summary>
    public static Task RunAsync(Func<DateTimeOffset, CancellationToken, Task<int>> purge, int days, Action<Exception> onError, CancellationToken ct)
        => RunAsync(purge, () => days, onError, ct);

    /// <summary>
    /// Runs a purge delegate once at startup and then daily, reading the days each round and continuing after a failed purge.
    /// </summary>
    public static async Task RunAsync(Func<DateTimeOffset, CancellationToken, Task<int>> purge, Func<int> days, Action<Exception> onError, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await PurgeOnceAsync(purge, days(), () => DateTimeOffset.UtcNow, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                onError(ex);
            }
        }
        while (await WaitAsync(timer, ct));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
