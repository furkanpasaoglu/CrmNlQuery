using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Tests.Contracts.Observability;

/// <summary>
/// Shared contract tests that every log store implementation, in-memory or PostgreSQL, must pass.
/// </summary>
public abstract class LogStoreContract : IAsyncLifetime
{
    protected abstract ILogStore CreateStore();

    private static readonly DateTimeOffset OldDay = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(new Random().Next(0, 3000));
    private static readonly DateTimeOffset TestEra = new DateTimeOffset(1950, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Prepares the log-store contract tests before each run.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Cleans up log-store contract test data after each run.
    /// </summary>
    public async Task DisposeAsync() => await CreateStore().PurgeOlderThanAsync(TestEra);

    private static string Marker() => "m" + Guid.NewGuid().ToString("N");

    private static LogEntry Entry(string message, DateTimeOffset? at = null, int level = LogLevels.Warning, string? exception = null, string? requestId = null)
        => new LogEntry(at ?? OldDay, level, message, exception, requestId);

    private static LogQuery Query(string marker, int minLevel = LogLevels.Verbose, DateTimeOffset? from = null, int take = 100)
        => new LogQuery(minLevel, marker, from, take);

    /// <summary>
    /// Verifies that an entry round trips with all its fields.
    /// </summary>
    [Fact]
    public async Task AnEntry_RoundTrips_WithAllItsFields()
    {
        var store = CreateStore();
        var marker = Marker();
        var at = OldDay.AddHours(12).AddMinutes(30).AddSeconds(45);
        await store.AddAsync(new[] { Entry(marker + " broke", at, LogLevels.Error, "System.Exception: boom", "req-123") });

        var found = (await store.QueryAsync(Query(marker))).Single();

        Assert.Equal(marker + " broke", found.Message);
        Assert.Equal(LogLevels.Error, found.Level);
        Assert.Equal("System.Exception: boom", found.Exception);
        Assert.Equal("req-123", found.RequestId);
        Assert.Equal(at, found.At);
    }

    /// <summary>
    /// Verifies that optional fields can be absent.
    /// </summary>
    [Fact]
    public async Task OptionalFields_CanBeAbsent()
    {
        var store = CreateStore();
        var marker = Marker();
        await store.AddAsync(new[] { Entry(marker) });

        var found = (await store.QueryAsync(Query(marker))).Single();

        Assert.Null(found.Exception);
        Assert.Null(found.RequestId);
    }

    /// <summary>
    /// Verifies that entries are returned newest first and limited.
    /// </summary>
    [Fact]
    public async Task Entries_AreReturnedNewestFirst_AndLimited()
    {
        var store = CreateStore();
        var marker = Marker();
        var start = OldDay;
        await store.AddAsync(new[]
        {
            Entry(marker + " one", start),
            Entry(marker + " three", start.AddSeconds(2)),
            Entry(marker + " two", start.AddSeconds(1)),
        });

        var all = await store.QueryAsync(Query(marker));
        var limited = await store.QueryAsync(Query(marker, take: 2));

        Assert.Equal(new[] { marker + " three", marker + " two", marker + " one" }, all.Select(e => e.Message));
        Assert.Equal(2, limited.Count);
        Assert.Equal(marker + " three", limited[0].Message);
    }

    /// <summary>
    /// Verifies that the minimum level hides less severe entries.
    /// </summary>
    [Fact]
    public async Task TheMinimumLevel_HidesLessSevereEntries()
    {
        var store = CreateStore();
        var marker = Marker();
        await store.AddAsync(new[]
        {
            Entry(marker + " info", level: LogLevels.Information),
            Entry(marker + " warn", level: LogLevels.Warning),
            Entry(marker + " error", level: LogLevels.Error),
        });

        var warnings = await store.QueryAsync(Query(marker, LogLevels.Warning));
        var errors = await store.QueryAsync(Query(marker, LogLevels.Error));

        Assert.Equal(new[] { marker + " error", marker + " warn" }, warnings.Select(e => e.Message).OrderBy(m => m, StringComparer.Ordinal));
        Assert.Equal(new[] { marker + " error" }, errors.Select(e => e.Message));
    }

    /// <summary>
    /// Verifies that text search ignores case and looks in message exception and request ID.
    /// </summary>
    [Fact]
    public async Task TextSearch_IgnoresCase_AndLooksInMessageExceptionAndRequestId()
    {
        var store = CreateStore();
        var marker = Marker();
        await store.AddAsync(new[]
        {
            Entry("alpha " + marker),
            Entry("plain", exception: "Beta " + marker + " failed"),
            Entry("other", requestId: marker),
        });

        Assert.Equal(3, (await store.QueryAsync(Query(marker.ToUpperInvariant()))).Count);
        Assert.Single(await store.QueryAsync(Query("ALPHA " + marker)));
        Assert.Single(await store.QueryAsync(Query("beta " + marker)));
    }

    /// <summary>
    /// Verifies that text search treats percent and underscore as plain characters.
    /// </summary>
    [Fact]
    public async Task TextSearch_TreatsPercentAndUnderscoreAsPlainCharacters()
    {
        var store = CreateStore();
        var marker = Marker();
        await store.AddAsync(new[] { Entry(marker), Entry(marker + "%x"), Entry(marker + "_y") });

        Assert.Equal(new[] { marker + "%x" }, (await store.QueryAsync(Query(marker + "%"))).Select(e => e.Message));
        Assert.Equal(new[] { marker + "_y" }, (await store.QueryAsync(Query(marker + "_"))).Select(e => e.Message));
    }

    /// <summary>
    /// Verifies that the from filter hides older entries.
    /// </summary>
    [Fact]
    public async Task TheFromFilter_HidesOlderEntries()
    {
        var store = CreateStore();
        var marker = Marker();
        var now = OldDay.AddHours(12);
        await store.AddAsync(new[] { Entry(marker + " old", now.AddHours(-5)), Entry(marker + " new", now.AddHours(-1)) });

        var recent = await store.QueryAsync(Query(marker, from: now.AddHours(-2)));

        Assert.Equal(new[] { marker + " new" }, recent.Select(e => e.Message));
    }

    /// <summary>
    /// Verifies that purge removes only entries older than the cutoff.
    /// </summary>
    [Fact]
    public async Task Purge_RemovesOnlyEntriesOlderThanTheCutoff()
    {
        var store = CreateStore();
        var marker = Marker();
        var day = OldDay;
        await store.AddAsync(new[] { Entry(marker + " old", day.AddHours(1)), Entry(marker + " newer", day.AddHours(23)) });

        var removed = await store.PurgeOlderThanAsync(day.AddHours(12));

        Assert.True(removed >= 1);
        Assert.Equal(new[] { marker + " newer" }, (await store.QueryAsync(Query(marker))).Select(e => e.Message));
    }

    /// <summary>
    /// Verifies that adding nothing is fine.
    /// </summary>
    [Fact]
    public async Task AddingNothing_IsFine()
    {
        var store = CreateStore();
        await store.AddAsync(new LogEntry[0]);
    }
}
