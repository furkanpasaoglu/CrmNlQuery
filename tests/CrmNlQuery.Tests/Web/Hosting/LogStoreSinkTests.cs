using Serilog;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Observability;
using CrmNlQuery.Web.Hosting;
using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Tests.Web.Hosting;

/// <summary>
/// Tests the Serilog sink that writes events to the admin log store without ever slowing or breaking requests.
/// </summary>
public class LogStoreSinkTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(10);

    private static ILogger LoggerFor(LogStoreSink sink)
        => new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

    private static Task<IReadOnlyList<LogEntry>> All(ILogStore store)
        => store.QueryAsync(new LogQuery(LogLevels.Verbose, null, null, 500));

    /// <summary>
    /// Verifies that events are written to the store with level message and request ID.
    /// </summary>
    [Fact]
    public async Task Events_AreWrittenToTheStore_WithLevelMessageAndRequestId()
    {
        var store = new MemoryLogStore();
        using var sink = new LogStoreSink(Fast);
        sink.Attach(store);
        var log = LoggerFor(sink);

        log.Warning("Request {RequestId} ended with {Kind}", "abc123", "crm_error");
        await sink.FlushAsync();

        var entry = (await All(store)).Single();
        Assert.Equal(LogLevels.Warning, entry.Level);
        Assert.Equal("Request \"abc123\" ended with \"crm_error\"", entry.Message);
        Assert.Equal("abc123", entry.RequestId);
        Assert.Null(entry.Exception);
    }

    /// <summary>
    /// Verifies that the exception text is kept.
    /// </summary>
    [Fact]
    public async Task TheExceptionText_IsKept()
    {
        var store = new MemoryLogStore();
        using var sink = new LogStoreSink(Fast);
        sink.Attach(store);

        LoggerFor(sink).Error(new InvalidOperationException("boom"), "It failed");
        await sink.FlushAsync();

        var entry = (await All(store)).Single();
        Assert.Equal(LogLevels.Error, entry.Level);
        Assert.Contains("InvalidOperationException", entry.Exception);
        Assert.Contains("boom", entry.Exception);
    }

    /// <summary>
    /// Verifies that events before the store is attached are not lost.
    /// </summary>
    [Fact]
    public async Task EventsBeforeTheStoreIsAttached_AreNotLost()
    {
        var store = new MemoryLogStore();
        using var sink = new LogStoreSink(Fast);
        var log = LoggerFor(sink);
        log.Warning("early one");
        log.Warning("early two");

        sink.Attach(store);
        await sink.FlushAsync();

        Assert.Equal(2, (await All(store)).Count);
    }

    /// <summary>
    /// Verifies that very long messages and exceptions are truncated.
    /// </summary>
    [Fact]
    public async Task VeryLongMessagesAndExceptions_AreTruncated()
    {
        var store = new MemoryLogStore();
        using var sink = new LogStoreSink(Fast);
        sink.Attach(store);

        LoggerFor(sink).Error(new Exception(new string('e', 50_000)), new string('m', 50_000));
        await sink.FlushAsync();

        var entry = (await All(store)).Single();
        Assert.True(entry.Message.Length <= LogStoreSink.MaxMessageLength + 10);
        Assert.True(entry.Exception!.Length <= LogStoreSink.MaxExceptionLength + 10);
    }

    /// <summary>
    /// Verifies that a failing store never breaks logging and later events still arrive.
    /// </summary>
    [Fact]
    public async Task AFailingStore_NeverBreaksLogging_AndLaterEventsStillArrive()
    {
        var flaky = new FlakyStore();
        using var sink = new LogStoreSink(Fast);
        sink.Attach(flaky);
        var log = LoggerFor(sink);

        log.Warning("lost with the failed batch");
        await sink.FlushAsync();
        log.Warning("arrives");
        await sink.FlushAsync();

        Assert.Equal(new[] { "arrives" }, flaky.Stored.Select(e => e.Message));
    }

    /// <summary>
    /// Verifies that when no store is ever attached logging never blocks or throws even far beyond the buffer.
    /// </summary>
    [Fact]
    public void WhenNoStoreIsEverAttached_LoggingNeverBlocksOrThrows_EvenFarBeyondTheBuffer()
    {
        using var sink = new LogStoreSink(Fast);
        var log = LoggerFor(sink);

        for (var i = 0; i < 20_000; i++)
        {
            log.Warning("flood {Number}", i);
        }
    }

    /// <summary>
    /// Verifies that dispose writes what is still waiting.
    /// </summary>
    [Fact]
    public async Task Dispose_WritesWhatIsStillWaiting()
    {
        var store = new MemoryLogStore();
        var sink = new LogStoreSink(TimeSpan.FromSeconds(30));
        sink.Attach(store);
        LoggerFor(sink).Warning("last words");

        sink.Dispose();

        Assert.Single(await All(store));
    }

    private sealed class FlakyStore : ILogStore
    {
        private int _calls;
        public List<LogEntry> Stored { get; } = new List<LogEntry>();

        /// <summary>
        /// Fake store method that records or fails entry writes to simulate a flaky database.
        /// </summary>
        public Task AddAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new InvalidOperationException("database down");
            }
            lock (Stored) { Stored.AddRange(entries); }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Fake store method that returns no entries for queries.
        /// </summary>
        public Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>
        /// Fake store method that purges nothing.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
