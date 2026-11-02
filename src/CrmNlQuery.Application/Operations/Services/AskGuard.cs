using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Models;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Application.Operations.Services;

/// <summary>
/// Single entry gate for question-running endpoints: applies the ask gate, end-to-end deadline and metrics, logging fixed failure kinds without question text.
/// </summary>
public sealed class AskGuard
{
    private readonly AskGate _gate;
    private readonly ILimitsSource _limits;
    private readonly IMetricsStore _metrics;
    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _clock;

    public AskGuard(AskGate gate, ILimitsSource limits, IMetricsStore metrics, ILogger? logger = null, Func<DateTimeOffset>? clock = null)
    {
        _gate = gate;
        _limits = limits;
        _metrics = metrics;
        _logger = logger ?? NullLogger.Instance;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Runs the question under the gate for a real user with quota, concurrency and deadline checks; the evaluation caller is not allowed here and throws.
    /// </summary>
    public async Task<GuardedResult> RunAsync(Caller callerIdentity, Func<CancellationToken, Task<AskOutcome?>> run, CancellationToken ct)
    {
        var caller = callerIdentity.UserId;
        GateDecision? decision = null;
        try
        {
            decision = _gate.TryEnter(caller);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The ask gate failed; the question is allowed through ({ExceptionType})", ex.GetType().Name);
        }

        if (decision is { Denial: { } denial })
        {
            _logger.LogWarning("Question refused: {Kind} (retry after {Seconds}s)", denial.Kind, denial.RetryAfterSeconds);
            await RecordAsync(caller, 0, denial.Kind);
            return new GuardedResult(null, denial);
        }

        using var lease = decision?.Lease;
        var seconds = _limits.Current.AskDeadlineSeconds;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        var started = _clock();
        using var watch = RequestContext.WatchDeadline(deadline.Token, ct);
        try
        {
            return new GuardedResult(await run(deadline.Token), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            _logger.LogWarning("Question stopped after the {Seconds}s deadline", seconds);
            await RecordAsync(caller, (long)(_clock() - started).TotalMilliseconds, "timeout");
            return new GuardedResult(new FailureOutcome(UserMessages.TimedOut(seconds), "timeout"), null);
        }
    }

    private async Task RecordAsync(Guid caller, long milliseconds, string kind)
    {
        try
        {
            await _metrics.RecordRequestAsync(new RequestRecord(Guid.NewGuid(), _clock(), caller, milliseconds, "failure", kind));
        }
        catch (Exception)
        {
        }
    }
}
