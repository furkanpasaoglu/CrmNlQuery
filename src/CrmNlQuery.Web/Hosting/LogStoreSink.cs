using System.Threading.Channels;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Writes Serilog events to the admin log store through a bounded queue and batched background writes, so logging never slows or breaks a request.
/// </summary>
public sealed class LogStoreSink : ILogEventSink, IDisposable
{
    public const int MaxMessageLength = 2000;
    public const int MaxExceptionLength = 8000;
    private const int QueueCapacity = 2000;
    private const int BatchSize = 200;

    private readonly TimeSpan _interval;
    private readonly Channel<LogEntry> _queue;
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private long _pending;
    private Task? _worker;
    private ILogStore? _store;

    public LogStoreSink(TimeSpan interval)
    {
        _interval = interval;
        _queue = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            dropped => Interlocked.Decrement(ref _pending));
    }

    /// <summary>
    /// Queues a log event, dropping the oldest when the queue is full.
    /// </summary>
    public void Emit(LogEvent logEvent)
    {
        string? requestId = null;
        if (logEvent.Properties.TryGetValue("RequestId", out var value) && value is ScalarValue scalar)
        {
            requestId = scalar.Value?.ToString();
        }
        var entry = new LogEntry(
            logEvent.Timestamp,
            (int)logEvent.Level,
            Truncate(logEvent.RenderMessage(), MaxMessageLength)!,
            Truncate(logEvent.Exception?.ToString(), MaxExceptionLength),
            requestId);

        Interlocked.Increment(ref _pending);
        if (!_queue.Writer.TryWrite(entry))
        {
            Interlocked.Decrement(ref _pending);
        }
    }

    /// <summary>
    /// Attaches the log store once it is ready and starts the background writer.
    /// </summary>
    public void Attach(ILogStore store)
    {
        _store = store;
        _worker = Task.Run(() => RunAsync(store));
    }

    /// <summary>
    /// Waits until everything queued has been written or attempted, for tests and shutdown.
    /// </summary>
    public async Task FlushAsync(TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (Interlocked.Read(ref _pending) > 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(5);
        }
    }

    private async Task RunAsync(ILogStore store)
    {
        var reader = _queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_stop.Token))
            {
                await Task.Delay(_interval, _stop.Token);
                await DrainAsync(store);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DrainAsync(ILogStore store)
    {
        var batch = new List<LogEntry>(BatchSize);
        while (batch.Count < BatchSize && _queue.Reader.TryRead(out var entry))
        {
            batch.Add(entry);
        }
        if (batch.Count == 0)
        {
            return;
        }
        try
        {
            await store.AddAsync(batch);
        }
        catch (Exception ex)
        {
            SelfLog.WriteLine("Could not write {0} log entries to the log store: {1}", batch.Count, ex.Message);
        }
        finally
        {
            Interlocked.Add(ref _pending, -batch.Count);
        }
        if (_queue.Reader.TryPeek(out _))
        {
            await DrainAsync(store);
        }
    }

    /// <summary>
    /// Tries to write whatever is still queued at shutdown, for at most a few seconds.
    /// </summary>
    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _stop.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
        if (_store is not null)
        {
            DrainAsync(_store).Wait(TimeSpan.FromSeconds(3));
        }
        _stop.Dispose();
    }

    private static string? Truncate(string? text, int max)
    {
        if (text is null || text.Length <= max)
        {
            return text;
        }
        return text.Substring(0, max) + "…";
    }
}
