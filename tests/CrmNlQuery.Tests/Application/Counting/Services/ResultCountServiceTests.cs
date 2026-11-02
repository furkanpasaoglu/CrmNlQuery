using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Counting.Abstractions;
using CrmNlQuery.Application.Counting.Models;
using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Counting.Services;

/// <summary>
/// Tests the result count service: ownership, the two count tiers, limit handling, timeout and errors, using a fake counter.
/// </summary>
public class ResultCountServiceTests
{
    private const string Fetch = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><order attribute=\"name\" /></entity></fetch>";

    private sealed class FakeCounter : IRecordCounter
    {
        public Func<long>? Aggregate { get; set; }
        public Func<RecordCount>? Total { get; set; }
        public Func<CancellationToken, Task>? Delay { get; set; }
        public List<(string Kind, string Set, string Fetch, Guid? Caller)> Calls { get; } = new List<(string, string, string, Guid?)>();

        /// <summary>
        /// Returns a fake aggregate count for the test.
        /// </summary>
        public async Task<long> AggregateCountAsync(string entitySetName, string aggregateFetchXml, Caller caller, CancellationToken ct = default)
        {
            Calls.Add(("aggregate", entitySetName, aggregateFetchXml, caller.UserIdOrNull()));
            if (Delay is not null) { await Delay(ct); }
            return (Aggregate ?? throw new CrmException("aggregate limit exceeded"))();
        }

        /// <summary>
        /// Returns a fake total record count for the test.
        /// </summary>
        public async Task<RecordCount> TotalCountAsync(string entitySetName, string totalCountFetchXml, Caller caller, CancellationToken ct = default)
        {
            Calls.Add(("total", entitySetName, totalCountFetchXml, caller.UserIdOrNull()));
            if (Delay is not null) { await Delay(ct); }
            return (Total ?? throw new CrmException("boom"))();
        }
    }

    private readonly MemoryHistoryStore _history = new MemoryHistoryStore();
    private readonly FakeCounter _counter = new FakeCounter();
    private TimeSpan _timeout = TimeSpan.FromSeconds(15);

    private ResultCountService Service()
    {
        var metadata = TestMetadata.Create();
        return new ResultCountService(_history, new CountQueryBuilder(new PlanValidator(metadata), metadata), _counter, () => _timeout);
    }

    private async Task<(Guid Owner, string Id)> Saved(string fetch = Fetch)
    {
        var owner = Guid.NewGuid();
        return (owner, await _history.AddAsync(owner, "q", "account", fetch));
    }

    /// <summary>
    /// Verifies that the aggregate count is the first choice and runs as the owner.
    /// </summary>
    [Fact]
    public async Task TheAggregateCount_IsTheFirstChoice_AndRunsAsTheOwner()
    {
        var (owner, id) = await Saved();
        _counter.Aggregate = () => 48312;

        var result = await Service().CountAsync(owner, id);

        Assert.Equal(CountStatus.Ok, result.Status);
        Assert.Equal(48312, result.Total);
        Assert.False(result.LimitExceeded);
        var call = Assert.Single(_counter.Calls);
        Assert.Equal("aggregate", call.Kind);
        Assert.Equal("accounts", call.Set);
        Assert.Equal(owner, call.Caller);
    }

    /// <summary>
    /// Verifies that when the aggregate fails the total record count takes over exact when within its limit.
    /// </summary>
    [Fact]
    public async Task WhenTheAggregateFails_TheTotalRecordCountTakesOver_ExactWhenWithinItsLimit()
    {
        var (owner, id) = await Saved();
        _counter.Total = () => new RecordCount(4200, false);

        var result = await Service().CountAsync(owner, id);

        Assert.Equal(CountStatus.Ok, result.Status);
        Assert.Equal(4200, result.Total);
        Assert.False(result.LimitExceeded);
        Assert.Equal(new[] { "aggregate", "total" }, _counter.Calls.Select(c => c.Kind).ToArray());
    }

    /// <summary>
    /// Verifies that when the CRM says the limit is exceeded the number it returns is used rather than a hardcoded one.
    /// </summary>
    [Fact]
    public async Task WhenTheCrmSaysTheLimitIsExceeded_TheNumberItReturnsIsUsed_NotAHardcodedOne()
    {
        var (owner, id) = await Saved();
        _counter.Total = () => new RecordCount(7777, true);

        var result = await Service().CountAsync(owner, id);

        Assert.Equal(CountStatus.Ok, result.Status);
        Assert.Equal(7777, result.Total);
        Assert.True(result.LimitExceeded);
    }

    /// <summary>
    /// Verifies that when both ways fail there is no number and nothing throws.
    /// </summary>
    [Fact]
    public async Task WhenBothWaysFail_ThereIsNoNumber_AndNothingThrows()
    {
        var (owner, id) = await Saved();

        var result = await Service().CountAsync(owner, id);

        Assert.Equal(CountStatus.Unavailable, result.Status);
    }

    /// <summary>
    /// Verifies that a permission or network error simply makes the count unavailable.
    /// </summary>
    [Fact]
    public async Task APermissionOrNetworkError_IsJustUnavailable()
    {
        var (owner, id) = await Saved();
        _counter.Aggregate = () => throw new CrmException("x", 403, isPermissionDenied: true);
        _counter.Total = () => throw new CrmException("Cannot reach CRM");

        Assert.Equal(CountStatus.Unavailable, (await Service().CountAsync(owner, id)).Status);
    }

    /// <summary>
    /// Verifies that the count has its own timeout after which there is no number.
    /// </summary>
    [Fact]
    public async Task TheCountHasItsOwnTimeout_AfterWhichThereIsNoNumber()
    {
        var (owner, id) = await Saved();
        _timeout = TimeSpan.FromMilliseconds(80);
        _counter.Delay = ct => Task.Delay(TimeSpan.FromSeconds(30), ct);
        _counter.Aggregate = () => 1;
        _counter.Total = () => new RecordCount(1, false);

        var result = await Service().CountAsync(owner, id);

        Assert.Equal(CountStatus.Unavailable, result.Status);
    }

    /// <summary>
    /// Verifies that the caller's own cancellation is not swallowed as a timeout.
    /// </summary>
    [Fact]
    public async Task TheCallersOwnCancellation_IsNotSwallowedAsATimeout()
    {
        var (owner, id) = await Saved();
        _counter.Delay = ct => Task.Delay(TimeSpan.FromSeconds(30), ct);
        _counter.Aggregate = () => 1;
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().CountAsync(owner, id, cts.Token));
    }

    /// <summary>
    /// Verifies that someone else's or an unknown result is not found and the CRM is never asked.
    /// </summary>
    [Fact]
    public async Task ASomeoneElsesOrUnknownResult_IsNotFound_AndTheCrmIsNeverAsked()
    {
        var (owner, id) = await Saved();
        _counter.Aggregate = () => 1;

        Assert.Equal(CountStatus.NotFound, (await Service().CountAsync(Guid.NewGuid(), id)).Status);
        Assert.Equal(CountStatus.NotFound, (await Service().CountAsync(owner, "nope")).Status);
        Assert.Equal(CountStatus.NotFound, (await Service().CountAsync(owner, Guid.NewGuid().ToString("N"))).Status);
        Assert.Empty(_counter.Calls);
    }

    /// <summary>
    /// Verifies that an aggregate result is not eligible for counting and the CRM is never asked.
    /// </summary>
    [Fact]
    public async Task AnAggregateResult_IsNotEligible_AndTheCrmIsNeverAsked()
    {
        var (owner, id) = await Saved("<fetch aggregate=\"true\"><entity name=\"account\"><attribute name=\"accountid\" alias=\"n\" aggregate=\"count\" /></entity></fetch>");

        var result = await Service().CountAsync(owner, id);

        Assert.Equal(CountStatus.NotEligible, result.Status);
        Assert.Empty(_counter.Calls);
    }

    /// <summary>
    /// Verifies that a stored query the validator no longer accepts is never sent to the CRM.
    /// </summary>
    [Fact]
    public async Task AStoredQueryTheValidatorNoLongerAccepts_IsNeverSentToTheCrm()
    {
        var (owner, id) = await Saved("<fetch top=\"500\"><entity name=\"account\"><attribute name=\"column_removed_from_crm\" /></entity></fetch>");

        var result = await Service().CountAsync(owner, id);

        Assert.Equal(CountStatus.NotEligible, result.Status);
        Assert.Empty(_counter.Calls);
    }

    private sealed class BrokenMetadata : CrmNlQuery.Application.Ask.Abstractions.IMetadataSource
    {
        /// <summary>
        /// Lists entities from the fake metadata source used by the test.
        /// </summary>
        public Task<IReadOnlyList<CrmNlQuery.Domain.Metadata.Entities.EntitySummary>> ListEntitiesAsync(CancellationToken ct = default) => throw new CrmException("down");
        /// <summary>
        /// Returns an entity from the fake metadata source used by the test.
        /// </summary>
        public Task<CrmNlQuery.Domain.Metadata.Entities.EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) => throw new CrmException("down");
        /// <summary>
        /// Refreshes the fake metadata source used by the test.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// Verifies that when metadata cannot be read there is no count but also no error.
    /// </summary>
    [Fact]
    public async Task WhenTheMetadataCanNotBeRead_ThereIsNoNumber_ButNoError()
    {
        var (owner, id) = await Saved();
        var broken = new BrokenMetadata();
        var service = new ResultCountService(_history, new CountQueryBuilder(new PlanValidator(broken), broken), _counter, () => _timeout);

        Assert.Equal(CountStatus.Unavailable, (await service.CountAsync(owner, id)).Status);
    }
}
