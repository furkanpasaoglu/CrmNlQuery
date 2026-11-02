using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Models;
using CrmNlQuery.Application.Operations.Services;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Tests.Application.Observability.Metrics;

/// <summary>
/// Tests that a user cancel writes a single cancelled record with stage and duration.
/// </summary>
public class MeteredAskCancelTests
{
    private sealed class SpyStore : IMetricsStore
    {
        public List<RequestRecord> Requests { get; } = new List<RequestRecord>();
        /// <summary>
        /// Stores the recorded model call for assertions.
        /// </summary>
        public Task RecordAiCallAsync(AiCallRecord record, CancellationToken ct = default) => Task.CompletedTask;
        /// <summary>
        /// Stores the recorded request for assertions.
        /// </summary>
        public Task RecordRequestAsync(RequestRecord record, CancellationToken ct = default)
        {
            Requests.Add(record);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Test spy store that returns an empty metrics summary, since the cancellation tests only inspect what was recorded.
        /// </summary>
        public Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>
        /// Does nothing and reports zero records removed.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class MutableLimits : ILimitsSource
    {
        public GuardrailLimits Current { get; set; } = new GuardrailLimits();
    }

    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly AskRequest Request = new AskRequest("secret question text", new Dictionary<string, string>(), Caller.User(User));

    private static MeteredAsk Metered(SpyStore store, Func<AskRequest, CancellationToken, Task<AskOutcome>> run)
        => new MeteredAsk(run, store, () => new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Verifies that when the caller aborts one cancelled record is written with the stage and the cancellation still propagates.
    /// </summary>
    [Fact]
    public async Task WhenTheCallerAborts_OneCancelledRecordIsWritten_WithTheStage_AndTheCancellationStillPropagates()
    {
        var store = new SpyStore();
        var metered = Metered(store, async (r, ct) =>
        {
            RequestContext.SetStage("plan");
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        });
        using var source = new CancellationTokenSource();
        var running = metered.RunAsync(Request, source.Token);
        source.CancelAfter(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        var record = Assert.Single(store.Requests);
        Assert.Equal("cancelled", record.Outcome);
        Assert.Equal("plan", record.ErrorType);
        Assert.Equal(User, record.User);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero), record.At);
        Assert.True(record.DurationMs >= 0);
    }

    /// <summary>
    /// Verifies that without any stage the cancelled record says unknown.
    /// </summary>
    [Fact]
    public async Task WithoutAnyStage_TheCancelledRecordSaysUnknown()
    {
        var store = new SpyStore();
        var metered = Metered(store, (r, ct) => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => metered.RunAsync(Request, default));

        Assert.Equal("unknown", Assert.Single(store.Requests).ErrorType);
    }

    /// <summary>
    /// Verifies that in a chain the stage carries the step number.
    /// </summary>
    [Fact]
    public async Task InAChain_TheStageCarriesTheStepNumber()
    {
        var store = new SpyStore();
        var metered = Metered(store, (r, ct) =>
        {
            RequestContext.SetChainStep(2);
            RequestContext.SetStage("crm");
            throw new OperationCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => metered.RunAsync(Request, default));

        Assert.Equal("step2:crm", Assert.Single(store.Requests).ErrorType);
    }

    /// <summary>
    /// Verifies that the cancelled record never contains the question text.
    /// </summary>
    [Fact]
    public async Task TheCancelledRecord_NeverContainsTheQuestionText()
    {
        var store = new SpyStore();
        var metered = Metered(store, (r, ct) => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => metered.RunAsync(Request, default));

        var text = store.Requests[0].ToString();
        Assert.DoesNotContain("secret question text", text);
    }

    /// <summary>
    /// Verifies that when the deadline passes only the timeout record is written no cancelled.
    /// </summary>
    [Fact]
    public async Task WhenTheDeadlinePasses_OnlyTheTimeoutRecordIsWritten_NoCancelled()
    {
        var limits = new MutableLimits { Current = new GuardrailLimits { AskDeadlineSeconds = 0 } };
        var guardStore = new SpyStore();
        var meteredStore = new SpyStore();
        var guard = new AskGuard(new AskGate(limits), limits, guardStore);
        var metered = Metered(meteredStore, async (r, ct) =>
        {
            RequestContext.SetStage("plan");
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        });

        var result = await guard.RunAsync(Request.Caller, async token => await metered.RunAsync(Request, token), default);

        Assert.Equal("timeout", Assert.IsType<FailureOutcome>(result.Outcome).Kind);
        Assert.Equal("timeout", Assert.Single(guardStore.Requests).ErrorType);
        Assert.Empty(meteredStore.Requests);
    }

    /// <summary>
    /// Verifies that when the caller aborts through the guard the lease is released the quota stays spent and only cancelled is recorded.
    /// </summary>
    [Fact]
    public async Task WhenTheCallerAbortsThroughTheGuard_TheLeaseIsReleased_TheQuotaStaysSpent_AndOnlyCancelledIsRecorded()
    {
        var limits = new MutableLimits();
        var guardStore = new SpyStore();
        var meteredStore = new SpyStore();
        var gate = new AskGate(limits);
        var guard = new AskGuard(gate, limits, guardStore);
        var metered = Metered(meteredStore, async (r, ct) =>
        {
            RequestContext.SetStage("decompose");
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        });
        using var source = new CancellationTokenSource();
        var running = guard.RunAsync(Request.Caller, async token => await metered.RunAsync(Request, token), source.Token);
        source.CancelAfter(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        Assert.Equal(0, gate.RunningTotal);
        Assert.Empty(guardStore.Requests);
        Assert.Equal("cancelled", Assert.Single(meteredStore.Requests).Outcome);
        limits.Current = new GuardrailLimits { QuestionsPerMinute = 1 };
        var again = await guard.RunAsync(Request.Caller, ct => Task.FromResult<AskOutcome?>(new FailureOutcome("x", "other")), default);
        Assert.Equal(GateDenial.RateLimited, again.Denial?.Kind);
    }
}
