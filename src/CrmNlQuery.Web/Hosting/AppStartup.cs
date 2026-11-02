using CrmNlQuery.Application.Operations.Startup;
using CrmNlQuery.Composition;

namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Tracks service setup so the web server can open before services are ready and show a waiting page instead of IIS 500.30, retrying in the background.
/// </summary>
public sealed class AppStartup
{
    private readonly object _gate = new object();
    private AppServices? _services;
    private StartupState _state = StartupState.Starting;
    private int _attempts;
    private DateTimeOffset _lastGateLog = DateTimeOffset.MinValue;

    public StartupState State
    {
        get { lock (_gate) { return _state; } }
    }

    public int Attempts
    {
        get { lock (_gate) { return _attempts; } }
    }

    public bool IsReady => State == StartupState.Ready;

    public AppServices Services
    {
        get
        {
            lock (_gate)
            {
                return _services ?? throw new InvalidOperationException("The service has not finished starting yet.");
            }
        }
    }

    /// <summary>
    /// Tells whether the waiting page may write a log entry, at most once a minute.
    /// </summary>
    public bool ShouldLogGateHit(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (now - _lastGateLog < TimeSpan.FromMinutes(1))
            {
                return false;
            }
            _lastGateLog = now;
            return true;
        }
    }

    /// <summary>
    /// Retries composing services until ready or shutdown, waiting only on unreachable dependencies and failing on any other error.
    /// </summary>
    public async Task RunAsync(
        Func<CancellationToken, Task<AppServices>> compose,
        Action<AppServices> onReady,
        ILogger logger,
        TimeSpan retryDelay,
        TimeSpan failedRetryDelay,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= (span, token) => Task.Delay(span, token);
        var started = DateTimeOffset.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            lock (_gate)
            {
                _attempts++;
            }
            try
            {
                var services = await compose(ct);
                onReady(services);
                lock (_gate)
                {
                    _services = services;
                    _state = StartupState.Ready;
                }
                logger.LogInformation("The service is ready after {Seconds} s ({Attempts} attempt(s)).", (int)(DateTimeOffset.UtcNow - started).TotalSeconds, Attempts);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (StartupDependencyException ex) when (ex.CanRetry)
            {
                lock (_gate)
                {
                    _state = StartupState.Starting;
                }
                logger.LogWarning("The service is not ready yet and keeps trying (attempt {Attempts}): {Message}", Attempts, ex.Message);
                await DelayAsync(delay, retryDelay, ct);
            }
            catch (StartupDependencyException ex)
            {
                lock (_gate)
                {
                    _state = StartupState.Failed;
                }
                logger.LogError(ex, "The service could not start (attempt {Attempts}): {Message}. It tries again every {Seconds} s; fix the cause and it recovers by itself.",
                    Attempts, ex.Message, (int)failedRetryDelay.TotalSeconds);
                await DelayAsync(delay, failedRetryDelay, ct);
            }
        }
    }

    private static async Task DelayAsync(Func<TimeSpan, CancellationToken, Task> delay, TimeSpan span, CancellationToken ct)
    {
        try
        {
            await delay(span, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}
