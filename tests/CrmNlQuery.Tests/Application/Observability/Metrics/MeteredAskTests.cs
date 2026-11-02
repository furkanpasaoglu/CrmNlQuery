using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Tests.Application.Observability.Metrics;

/// <summary>
/// Tests that metered ask records request duration and outcome kind.
/// </summary>
public class MeteredAskTests
{
    private sealed class RecordingStore : IMetricsStore
    {
        public List<RequestRecord> Requests { get; } = new List<RequestRecord>();
        public Exception? ThrowOnRecord { get; set; }
        /// <summary>
        /// Stores the recorded model call for assertions.
        /// </summary>
        public Task RecordAiCallAsync(AiCallRecord record, CancellationToken ct = default) => Task.CompletedTask;
        /// <summary>
        /// Stores the recorded request for assertions.
        /// </summary>
        public Task RecordRequestAsync(RequestRecord record, CancellationToken ct = default)
        {
            if (ThrowOnRecord is not null) throw ThrowOnRecord;
            Requests.Add(record);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Test store that returns an empty metrics summary, since these ask metering tests only inspect the recorded rows.
        /// </summary>
        public Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>
        /// Does nothing and reports zero records removed.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly AskRequest Request = new AskRequest("q", new Dictionary<string, string>(), Caller.User(Guid.Parse("11111111-1111-1111-1111-111111111111")));

    private static AnswerOutcome Answer()
        => new AnswerOutcome("account", "u", "<fetch/>", new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>()), false);

    private static MeteredAsk Build(RecordingStore store, Func<AskRequest, CancellationToken, Task<AskOutcome>> run)
        => new MeteredAsk(run, store, () => new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Verifies that answer is recorded with the user and the outcome is returned unchanged.
    /// </summary>
    [Fact]
    public async Task Answer_IsRecorded_WithTheUser_AndTheOutcomeIsReturnedUnchanged()
    {
        var store = new RecordingStore();
        var answer = Answer();
        var outcome = await Build(store, (r, ct) => Task.FromResult<AskOutcome>(answer)).RunAsync(Request, default);

        Assert.Equal(answer with { DurationMs = ((AnswerOutcome)outcome).DurationMs }, outcome);
        Assert.NotNull(((AnswerOutcome)outcome).DurationMs);
        var record = Assert.Single(store.Requests);
        Assert.Equal("answer", record.Outcome);
        Assert.Null(record.ErrorType);
        Assert.Equal(Request.Caller.UserId, record.User);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero), record.At);
        Assert.True(record.DurationMs >= 0);
    }

    /// <summary>
    /// Verifies that question is recorded as a question.
    /// </summary>
    [Fact]
    public async Task Question_IsRecordedAsAQuestion()
    {
        var store = new RecordingStore();
        var question = new QuestionOutcome("columns", "columns", "Which?", new List<Choice>(), true);
        await Build(store, (r, ct) => Task.FromResult<AskOutcome>(question)).RunAsync(Request, default);
        Assert.Equal("question", Assert.Single(store.Requests).Outcome);
    }

    /// <summary>
    /// Verifies that failure is recorded with its kind.
    /// </summary>
    [Fact]
    public async Task Failure_IsRecorded_WithItsKind()
    {
        var store = new RecordingStore();
        await Build(store, (r, ct) => Task.FromResult<AskOutcome>(new FailureOutcome("x", "crm_error"))).RunAsync(Request, default);
        var record = Assert.Single(store.Requests);
        Assert.Equal("failure", record.Outcome);
        Assert.Equal("crm_error", record.ErrorType);
    }

    /// <summary>
    /// Verifies that the request context is set while the question runs and cleared after.
    /// </summary>
    [Fact]
    public async Task TheRequestContext_IsSetWhileTheQuestionRuns_AndClearedAfter()
    {
        var store = new RecordingStore();
        Guid seen = Guid.Empty;
        await Build(store, (r, ct) => { seen = RequestContext.Current; return Task.FromResult<AskOutcome>(Answer()); }).RunAsync(Request, default);

        Assert.NotEqual(Guid.Empty, seen);
        Assert.Equal(seen, Assert.Single(store.Requests).Id);
        Assert.Equal(Guid.Empty, RequestContext.Current);
    }

    /// <summary>
    /// Verifies that a failing metrics store never breaks the answer.
    /// </summary>
    [Fact]
    public async Task AFailingMetricsStore_NeverBreaksTheAnswer()
    {
        var store = new RecordingStore { ThrowOnRecord = new InvalidOperationException("db down") };
        var outcome = await Build(store, (r, ct) => Task.FromResult<AskOutcome>(Answer())).RunAsync(Request, default);
        Assert.IsType<AnswerOutcome>(outcome);
    }

    /// <summary>
    /// Verifies that an unexpected crash is recorded as unexpected and still thrown.
    /// </summary>
    [Fact]
    public async Task AnUnexpectedCrash_IsRecordedAsUnexpected_AndStillThrown()
    {
        var store = new RecordingStore();
        var run = Build(store, (r, ct) => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.RunAsync(Request, default));

        var record = Assert.Single(store.Requests);
        Assert.Equal("failure", record.Outcome);
        Assert.Equal("unexpected", record.ErrorType);
    }

    /// <summary>
    /// Verifies that caller cancellation is recorded as cancelled not as a failure.
    /// </summary>
    [Fact]
    public async Task CallerCancellation_IsRecordedAsCancelled_NotAsAFailure()
    {
        var store = new RecordingStore();
        var run = Build(store, (r, ct) => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.RunAsync(Request, default));

        var record = Assert.Single(store.Requests);
        Assert.Equal("cancelled", record.Outcome);
    }
}
