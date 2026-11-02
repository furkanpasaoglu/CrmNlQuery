using CrmNlQuery.Composition;

namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Starts service setup and lets the server listen ready if setup finishes quickly, otherwise serves the waiting page.
/// </summary>
public sealed class StartupHostedService : IHostedService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FailedRetryDelay = TimeSpan.FromSeconds(30);

    private readonly AppStartup _startup;
    private readonly Func<CancellationToken, Task<AppServices>> _compose;
    private readonly Action<AppServices> _onReady;
    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TimeSpan _syncWait;
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
    private Task _run = Task.CompletedTask;

    public StartupHostedService(AppStartup startup, Func<CancellationToken, Task<AppServices>> compose, Action<AppServices> onReady, ILogger logger, IHostApplicationLifetime lifetime, TimeSpan syncWait)
    {
        _startup = startup;
        _compose = compose;
        _onReady = onReady;
        _logger = logger;
        _lifetime = lifetime;
        _syncWait = syncWait;
    }

    /// <summary>
    /// Starts the background service setup and waits briefly for it to finish.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _run = Task.Run(() => _startup.RunAsync(_compose, _onReady, _logger, RetryDelay, FailedRetryDelay, _stopping.Token));
        await Task.WhenAny(_run, Task.Delay(_syncWait, cancellationToken));
        if (_run.IsCompleted)
        {
            await _run;
        }
        else
        {
            _ = _run.ContinueWith(task =>
            {
                _logger.LogCritical(task.Exception!.GetBaseException(), "The service could not start and is stopping.");
                _lifetime.StopApplication();
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
        if (!_startup.IsReady)
        {
            _logger.LogWarning("The service is not ready after {Seconds} s; the web server starts anyway and shows a waiting page until it is.", (int)_syncWait.TotalSeconds);
        }
    }

    /// <summary>
    /// Signals the background startup work to cancel and waits up to five seconds for it to finish before returning.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        await Task.WhenAny(_run, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
    }
}
