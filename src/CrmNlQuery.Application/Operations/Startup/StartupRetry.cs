using System.Diagnostics;

namespace CrmNlQuery.Application.Operations.Startup;

/// <summary>
/// Retries a dependent service at intervals during startup instead of crashing at once.
/// </summary>
public static class StartupRetry
{
    /// <summary>
    /// Runs a probe until it succeeds, retrying at intervals up to a maximum wait and giving up on permanent errors.
    /// </summary>
    public static async Task WaitAsync(
        string name,
        Func<CancellationToken, Task> probe,
        TimeSpan maxWait,
        TimeSpan interval,
        Action<int, Exception, TimeSpan>? onRetry = null,
        Func<Exception, bool>? isTransient = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<TimeSpan>? elapsed = null,
        CancellationToken ct = default)
    {
        delay ??= (span, token) => Task.Delay(span, token);
        var clock = Stopwatch.StartNew();
        elapsed ??= () => clock.Elapsed;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await probe(ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (isTransient is not null && !isTransient(ex))
                {
                    throw new StartupDependencyException($"{name} refused the connection and retrying will not help ({ex.GetType().Name}: {ex.Message}).", canRetry: false, ex);
                }
                if (elapsed() + interval > maxWait)
                {
                    throw new StartupDependencyException(
                        $"{name} did not become reachable within {maxWait.TotalSeconds:F0} seconds ({attempt} attempt(s)). Last error: {ex.GetType().Name}: {ex.Message}", canRetry: true, ex);
                }
                onRetry?.Invoke(attempt, ex, interval);
                await delay(interval, ct);
            }
        }
    }
}
