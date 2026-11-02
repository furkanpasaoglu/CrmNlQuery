using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Application.Observability.Services;

/// <summary>
/// Reports service health for CRM, database and model, each check with a time limit.
/// </summary>
public sealed class HealthService
{
    private readonly IReadOnlyList<HealthCheck> _checks;
    private readonly TimeSpan _timeout;

    public HealthService(IReadOnlyList<HealthCheck> checks, TimeSpan timeout)
    {
        _checks = checks;
        _timeout = timeout;
    }

    /// <summary>
    /// Runs all health checks, each with a timeout, and returns their results.
    /// </summary>
    public async Task<IReadOnlyList<HealthItem>> CheckAsync(CancellationToken ct = default)
    {
        var tasks = _checks.Select(c => RunAsync(c, ct)).ToList();
        return (await Task.WhenAll(tasks)).ToList();
    }

    private async Task<HealthItem> RunAsync(HealthCheck check, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_timeout);
        try
        {
            await check.Run(limit.Token);
            return new HealthItem(check.Name, true, null, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new HealthItem(check.Name, false, $"timed out after {_timeout.TotalSeconds:0.#} s", watch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = ex.Message.Length > 200 ? ex.Message.Substring(0, 200) : ex.Message;
            return new HealthItem(check.Name, false, ex.GetType().Name + ": " + message, watch.ElapsedMilliseconds);
        }
    }
}
