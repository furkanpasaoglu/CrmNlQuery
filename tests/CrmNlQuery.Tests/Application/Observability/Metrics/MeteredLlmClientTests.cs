using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Observability.Metrics;

/// <summary>
/// Tests that the metered model client records each model call with step, tokens and outcome.
/// </summary>
public class MeteredLlmClientTests
{
    private sealed class RecordingStore : IMetricsStore
    {
        public List<AiCallRecord> Calls { get; } = new List<AiCallRecord>();
        public Exception? ThrowOnRecord { get; set; }
        /// <summary>
        /// Stores the recorded model call for assertions.
        /// </summary>
        public Task RecordAiCallAsync(AiCallRecord record, CancellationToken ct = default)
        {
            if (ThrowOnRecord is not null) throw ThrowOnRecord;
            Calls.Add(record);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Stores the recorded request for assertions.
        /// </summary>
        public Task RecordRequestAsync(RequestRecord record, CancellationToken ct = default) => Task.CompletedTask;
        /// <summary>
        /// Test store that returns an empty metrics summary, since these metering tests only inspect the recorded calls.
        /// </summary>
        public Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>
        /// Does nothing and reports zero records removed.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class InnerClient : ILlmClient
    {
        public Func<LlmResult>? Reply { get; set; }
        public Exception? Throw { get; set; }
        /// <summary>
        /// Returns scripted model output for the test instead of calling a real model.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
            => throw new InvalidOperationException("The decorator must call CompleteAsync.");
        /// <summary>
        /// Returns scripted model output for the test instead of calling a real model.
        /// </summary>
        public Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult(Reply!());
        }
    }

    private static (MeteredLlmClient Client, RecordingStore Store, InnerClient Inner) Create()
    {
        var store = new RecordingStore();
        var inner = new InnerClient { Reply = () => new LlmResult("{\"ok\":true}", new LlmUsage(120, 34)) };
        return (new MeteredLlmClient(inner, store, "plan", "TestProvider", "m-test"), store, inner);
    }

    /// <summary>
    /// Verifies that success is recorded with usage purpose and model and the text is unchanged.
    /// </summary>
    [Fact]
    public async Task Success_IsRecorded_WithUsagePurposeAndModel_AndTheTextIsUnchanged()
    {
        var (client, store, _) = Create();
        var text = await client.CompleteJsonAsync("s", "u", "{}");

        Assert.Equal("{\"ok\":true}", text);
        var record = Assert.Single(store.Calls);
        Assert.Equal("plan", record.Purpose);
        Assert.Equal("TestProvider", record.Provider);
        Assert.Equal("m-test", record.Model);
        Assert.Equal(120, record.InputTokens);
        Assert.Equal(34, record.OutputTokens);
        Assert.Equal("ok", record.Outcome);
        Assert.Null(record.ErrorType);
        Assert.True(record.DurationMs >= 0);
    }

    /// <summary>
    /// Verifies that prompt size is recorded as chars and an estimated token count.
    /// </summary>
    [Fact]
    public async Task PromptSize_IsRecorded_AsCharsAndAnEstimatedTokenCount()
    {
        var (client, store, _) = Create();

        await client.CompleteJsonAsync(new string('s', 1000), new string('u', 2000), new string('x', 500));

        var record = Assert.Single(store.Calls);
        Assert.Equal(3500, record.PromptChars);
        Assert.Equal(1000, record.EstTokens);
    }

    /// <summary>
    /// Verifies that context overflow is recorded with its cause the token split and still thrown.
    /// </summary>
    [Theory]
    [InlineData(PromptTooLongException.PromptCause, "context_overflow_prompt")]
    [InlineData(PromptTooLongException.GenerationCause, "context_overflow_generation")]
    public async Task ContextOverflow_IsRecordedWithItsCause_TheTokenSplit_AndStillThrown(string cause, string expectedType)
    {
        var (client, store, inner) = Create();
        inner.Throw = new PromptTooLongException(cause, 400, 595, 1000);

        await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteJsonAsync("s", "u", "{}"));

        var record = Assert.Single(store.Calls);
        Assert.Equal("error", record.Outcome);
        Assert.Equal(expectedType, record.ErrorType);
        Assert.Equal(400, record.InputTokens);
        Assert.Equal(595, record.OutputTokens);
        Assert.Equal(4, record.PromptChars);
    }

    /// <summary>
    /// Verifies that one information line per call has sizes and duration but no prompt text.
    /// </summary>
    [Fact]
    public async Task OneInformationLine_PerCall_HasSizesAndDuration_ButNoPromptText()
    {
        var store = new RecordingStore();
        var inner = new InnerClient { Reply = () => new LlmResult("{\"ok\":true}", new LlmUsage(120, 34)) };
        var logger = new ListLogger();
        var client = new MeteredLlmClient(inner, store, "plan", "TestProvider", "m-test", logger: logger);

        await client.CompleteJsonAsync("SECRET-SYSTEM", "SECRET-USER", "{}");

        var line = Assert.Single(logger.Lines);
        Assert.Contains("plan", line);
        Assert.Contains("m-test", line);
        Assert.Contains("120", line);
        Assert.Contains("34", line);
        Assert.DoesNotContain("SECRET", line);
    }

    private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Lines { get; } = new List<string>();
        /// <summary>
        /// Fake logger scope that does nothing.
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>
        /// Reports every log level as enabled.
        /// </summary>
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        /// <summary>
        /// Stores the log message in a list for assertions.
        /// </summary>
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception));
    }

    /// <summary>
    /// Verifies that unavailable model is recorded as an error and still thrown.
    /// </summary>
    [Fact]
    public async Task UnavailableModel_IsRecordedAsAnError_AndStillThrown()
    {
        var (client, store, inner) = Create();
        inner.Throw = new LlmUnavailableException("down");

        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteJsonAsync("s", "u", "{}"));

        var record = Assert.Single(store.Calls);
        Assert.Equal("error", record.Outcome);
        Assert.Equal("unavailable", record.ErrorType);
    }

    /// <summary>
    /// Verifies that any other failure is recorded as other and still thrown.
    /// </summary>
    [Fact]
    public async Task AnyOtherFailure_IsRecordedAsOther_AndStillThrown()
    {
        var (client, store, inner) = Create();
        inner.Throw = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteJsonAsync("s", "u", "{}"));

        Assert.Equal("other", Assert.Single(store.Calls).ErrorType);
    }

    /// <summary>
    /// Verifies that request ID comes from the current request context.
    /// </summary>
    [Fact]
    public async Task RequestId_ComesFromTheCurrentRequestContext()
    {
        var (client, store, _) = Create();
        var id = Guid.NewGuid();
        using (RequestContext.Begin(id))
        {
            await client.CompleteJsonAsync("s", "u", "{}");
        }
        await client.CompleteJsonAsync("s", "u", "{}");

        Assert.Equal(id, store.Calls[0].RequestId);
        Assert.Equal(Guid.Empty, store.Calls[1].RequestId);
    }

    /// <summary>
    /// Verifies that a failing metrics store never breaks the call.
    /// </summary>
    [Fact]
    public async Task AFailingMetricsStore_NeverBreaksTheCall()
    {
        var (client, store, _) = Create();
        store.ThrowOnRecord = new InvalidOperationException("db down");

        var text = await client.CompleteJsonAsync("s", "u", "{}");

        Assert.Equal("{\"ok\":true}", text);
    }

    /// <summary>
    /// Verifies that caller cancellation is not recorded as a model error.
    /// </summary>
    [Fact]
    public async Task CallerCancellation_IsNotRecordedAsAModelError()
    {
        var (client, store, inner) = Create();
        inner.Throw = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteJsonAsync("s", "u", "{}"));

        Assert.Empty(store.Calls);
    }
}
