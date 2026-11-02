using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Models;
using CrmNlQuery.Application.Operations.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Observability.Metrics;

namespace CrmNlQuery.Tests.Application.Operations.Services;

/// <summary>
/// Tests the guard that applies quotas, concurrency and deadlines to ask requests.
/// </summary>
public class AskGuardTests
{
    private sealed class MutableLimits : ILimitsSource
    {
        public GuardrailLimits Current { get; set; } = new GuardrailLimits();
    }

    private sealed class SpyMetrics : IMetricsStore
    {
        public List<RequestRecord> Requests { get; } = new List<RequestRecord>();
        public bool Broken { get; set; }

        /// <summary>
        /// Fake metrics store method that records an AI call.
        /// </summary>
        public Task RecordAiCallAsync(AiCallRecord record, CancellationToken ct = default) => Task.CompletedTask;

        /// <summary>
        /// Fake metrics store method that records a request.
        /// </summary>
        public Task RecordRequestAsync(RequestRecord record, CancellationToken ct = default)
        {
            if (Broken) throw new InvalidOperationException("db down");
            Requests.Add(record);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Fake metrics store method that returns a summary.
        /// </summary>
        public Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>
        /// Fake metrics store method that purges older records.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private readonly MutableLimits _limits = new MutableLimits();
    private readonly SpyMetrics _metrics = new SpyMetrics();
    private readonly Guid _user = Guid.NewGuid();

    private (AskGuard Guard, AskGate Gate) Make()
    {
        var gate = new AskGate(_limits);
        return (new AskGuard(gate, _limits, _metrics), gate);
    }

    private static Task<AskOutcome?> Fail(CancellationToken ct) => Task.FromResult<AskOutcome?>(new FailureOutcome("x", "other"));

    /// <summary>
    /// Verifies that an allowed question runs and returns its outcome and the lease is released.
    /// </summary>
    [Fact]
    public async Task AnAllowedQuestion_RunsAndReturnsItsOutcome_AndTheLeaseIsReleased()
    {
        var (guard, gate) = Make();

        var result = await guard.RunAsync(Caller.User(_user), Fail, default);

        Assert.Null(result.Denial);
        Assert.IsType<FailureOutcome>(result.Outcome);
        Assert.Equal(0, gate.RunningTotal);
        Assert.Empty(_metrics.Requests);
    }

    /// <summary>
    /// Verifies that a denied question never runs and is recorded with a fixed kind.
    /// </summary>
    [Fact]
    public async Task ADeniedQuestion_NeverRuns_AndIsRecordedWithAFixedKind()
    {
        _limits.Current = new GuardrailLimits { AskEnabled = false };
        var (guard, _) = Make();
        var ran = false;

        var result = await guard.RunAsync(Caller.User(_user), ct => { ran = true; return Fail(ct); }, default);

        Assert.False(ran);
        Assert.Null(result.Outcome);
        Assert.Equal(GateDenial.AsksPaused, result.Denial!.Kind);
        var record = Assert.Single(_metrics.Requests);
        Assert.Equal("failure", record.Outcome);
        Assert.Equal("asks_paused", record.ErrorType);
        Assert.Equal(_user, record.User);
    }

    /// <summary>
    /// Verifies that a recording failure never breaks the refusal.
    /// </summary>
    [Fact]
    public async Task ARecordingFailure_NeverBreaksTheRefusal()
    {
        _limits.Current = new GuardrailLimits { AskEnabled = false };
        _metrics.Broken = true;
        var (guard, _) = Make();

        var result = await guard.RunAsync(Caller.User(_user), Fail, default);

        Assert.NotNull(result.Denial);
    }

    /// <summary>
    /// Verifies that a per-user limit refuses a second parallel question while the first is still running.
    /// </summary>
    [Fact]
    public async Task APerUserLimit_RefusesTheSecondParallelQuestion_WhileTheFirstIsStillRunning()
    {
        _limits.Current = new GuardrailLimits { MaxConcurrentPerUser = 1 };
        var (guard, gate) = Make();
        var release = new TaskCompletionSource();
        var first = guard.RunAsync(Caller.User(_user), async ct => { await release.Task; return await Fail(ct); }, default);

        var second = await guard.RunAsync(Caller.User(_user), Fail, default);
        release.SetResult();
        await first;

        Assert.Equal(GateDenial.BusyUser, second.Denial!.Kind);
        Assert.Equal(0, gate.RunningTotal);
    }

    /// <summary>
    /// Verifies that when the deadline passes the question is stopped with a clear timeout outcome and recorded.
    /// </summary>
    [Fact]
    public async Task WhenTheDeadlinePasses_TheQuestionIsStopped_WithAClearTimeoutOutcome_AndRecorded()
    {
        _limits.Current = new GuardrailLimits { AskDeadlineSeconds = 0 };
        var (guard, gate) = Make();

        var result = await guard.RunAsync(Caller.User(_user), async ct => { await Task.Delay(Timeout.Infinite, ct); return null; }, default);

        var failure = Assert.IsType<FailureOutcome>(result.Outcome);
        Assert.Equal("timeout", failure.Kind);
        Assert.Contains("took longer", failure.Message);
        Assert.Equal("timeout", Assert.Single(_metrics.Requests).ErrorType);
        Assert.Equal(0, gate.RunningTotal);
    }

    /// <summary>
    /// Verifies that a user who cancels is not treated as a timeout and the lease is still released.
    /// </summary>
    [Fact]
    public async Task AUserWhoCancels_IsNotTreatedAsATimeout_AndTheLeaseIsStillReleased()
    {
        var (guard, gate) = Make();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => guard.RunAsync(Caller.User(_user), async ct => { await Task.Delay(Timeout.Infinite, ct); return null; }, cts.Token));

        Assert.Equal(0, gate.RunningTotal);
        Assert.Empty(_metrics.Requests);
    }

    /// <summary>
    /// Verifies that an exception inside the question still releases the lease.
    /// </summary>
    [Fact]
    public async Task AnExceptionInsideTheQuestion_StillReleasesTheLease()
    {
        var (guard, gate) = Make();

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.RunAsync(Caller.User(_user), _ => throw new InvalidOperationException("boom"), default));

        Assert.Equal(0, gate.RunningTotal);
    }

    /// <summary>
    /// Verifies that a null outcome for example a missing result to refine is passed through.
    /// </summary>
    [Fact]
    public async Task ANullOutcome_ForExampleAMissingResultToRefine_IsPassedThrough()
    {
        var (guard, _) = Make();

        var result = await guard.RunAsync(Caller.User(_user), _ => Task.FromResult<AskOutcome?>(null), default);

        Assert.Null(result.Outcome);
        Assert.Null(result.Denial);
    }
}
