using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Infrastructure.Persistence.Memory.Observability;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Observability.AccessLog;

/// <summary>
/// Unit tests for data-access logging: every read run on a user's behalf is recorded with tables, row count and source, never with question text or values.
/// </summary>
public class AccessLoggingTests
{
    private const string Fetch = "<fetch><entity name=\"contract\"><link-entity name=\"account\" from=\"accountid\" to=\"customerid\" /><filter><condition attribute=\"name\" operator=\"eq\" value=\"Örnekova\" /></filter></entity></fetch>";
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeExecutor : IQueryExecutor
    {
        public Exception? Throw;
        public int Rows = 3;

        /// <summary>
        /// Test double that execute with canned data instead of calling a real dependency.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            if (Throw is not null) { throw Throw; }
            var rows = Enumerable.Range(0, Rows).Select(i => (IReadOnlyList<string>)new[] { "r" + i }).ToList();
            return Task.FromResult(new ResultTable(new[] { "name" }, rows));
        }
    }

    private sealed class FakeSearcher : IRecordSearcher
    {
        public Exception? Throw;
        public int Matches = 2;

        /// <summary>
        /// Test double that search by name with canned data instead of calling a real dependency.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
        {
            if (Throw is not null) { throw Throw; }
            return Task.FromResult<IReadOnlyList<RecordMatch>>(Enumerable.Range(0, Matches).Select(i => new RecordMatch(Guid.NewGuid(), "name" + i)).ToList());
        }
    }

    private sealed class BrokenStore : IAccessLogStore
    {
        /// <summary>
        /// Test double access-log store whose add method throws, proving that a logging failure never breaks the caller.
        /// </summary>
        public Task AddAsync(DataAccessRecord record, CancellationToken ct = default) => throw new InvalidOperationException("db down");
        /// <summary>
        /// Test double access-log store whose query method throws, simulating an unavailable log store.
        /// </summary>
        public Task<IReadOnlyList<DataAccessRecord>> QueryAsync(DataAccessQuery query, CancellationToken ct = default) => throw new InvalidOperationException("db down");
        /// <summary>
        /// Test double access-log store whose purge method throws, simulating an unavailable log store.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) => throw new InvalidOperationException("db down");
    }

    private readonly MemoryAccessLogStore _store = new MemoryAccessLogStore();
    private readonly Guid _user = Guid.NewGuid();

    private AccessLoggingQueryExecutor Executor(FakeExecutor inner, IAccessLogStore? store = null)
        => new AccessLoggingQueryExecutor(inner, store ?? _store, () => Now);

    private async Task<IReadOnlyList<DataAccessRecord>> AllAsync()
        => await _store.QueryAsync(new DataAccessQuery(Now.AddDays(-1), Now.AddDays(1), null, null, 100));

    /// <summary>
    /// Verifies that a query is recorded with user tables rows and the ask source and the result is unchanged.
    /// </summary>
    [Fact]
    public async Task AQuery_IsRecorded_WithUserTablesRowsAndTheAskSource_AndTheResultIsUnchanged()
    {
        var requestId = Guid.NewGuid();
        ResultTable table;
        using (AccessLogContext.Begin("ask", requestId))
        {
            table = await Executor(new FakeExecutor { Rows = 4 }).ExecuteAsync("contracts", Fetch, Caller.User(_user));
        }

        Assert.Equal(4, table.Rows.Count);
        var record = Assert.Single(await AllAsync());
        Assert.Equal(_user, record.User);
        Assert.Equal(Now, record.At);
        Assert.Equal("ask", record.Source);
        Assert.Equal(new[] { "contract", "account" }, record.Tables);
        Assert.Equal(4, record.Rows);
        Assert.Equal("ok", record.Outcome);
        Assert.Equal(requestId, record.RequestId);
    }

    /// <summary>
    /// Verifies that every query of one request shares the request ID so the rows group.
    /// </summary>
    [Fact]
    public async Task EveryQueryOfOneRequest_SharesTheRequestId_SoTheRowsGroup()
    {
        var requestId = Guid.NewGuid();
        using (AccessLogContext.Begin("ask", requestId))
        {
            var executor = Executor(new FakeExecutor());
            await executor.ExecuteAsync("contracts", Fetch, Caller.User(_user));
            await executor.ExecuteAsync("accounts", "<fetch><entity name=\"account\" /></fetch>", Caller.User(_user));
        }

        var records = await AllAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(requestId, r.RequestId));
    }

    /// <summary>
    /// Verifies that without a context the source is other and the record gets its own request ID.
    /// </summary>
    [Fact]
    public async Task WithoutAContext_TheSourceIsOther_AndTheRecordGetsItsOwnRequestId()
    {
        await Executor(new FakeExecutor()).ExecuteAsync("contracts", Fetch, Caller.User(_user));
        await Executor(new FakeExecutor()).ExecuteAsync("contracts", Fetch, Caller.User(_user));

        var records = await AllAsync();
        Assert.All(records, r => Assert.Equal("other", r.Source));
        Assert.NotEqual(records[0].RequestId, records[1].RequestId);
    }

    /// <summary>
    /// Verifies that the context ends when its scope is disposed.
    /// </summary>
    [Fact]
    public async Task TheContextEnds_WhenItsScopeIsDisposed()
    {
        using (AccessLogContext.Begin("result")) { }

        await Executor(new FakeExecutor()).ExecuteAsync("contracts", Fetch, Caller.User(_user));

        Assert.Equal("other", Assert.Single(await AllAsync()).Source);
    }

    /// <summary>
    /// Verifies that a denied query is recorded as CRM denied and the exception still flows.
    /// </summary>
    [Fact]
    public async Task ADeniedQuery_IsRecordedAsCrmDenied_AndTheExceptionStillFlows()
    {
        var denied = new CrmException("You do not have permission to read Product records.", 403, null, true);

        var thrown = await Assert.ThrowsAsync<CrmException>(() => Executor(new FakeExecutor { Throw = denied }).ExecuteAsync("contracts", Fetch, Caller.User(_user)));

        Assert.Same(denied, thrown);
        var record = Assert.Single(await AllAsync());
        Assert.Equal("crm_denied", record.Outcome);
        Assert.Equal(0, record.Rows);
    }

    /// <summary>
    /// Verifies that any other failure is recorded as error and rethrown.
    /// </summary>
    [Fact]
    public async Task AnyOtherFailure_IsRecordedAsError_AndRethrown()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Executor(new FakeExecutor { Throw = new InvalidOperationException("boom") }).ExecuteAsync("contracts", Fetch, Caller.User(_user)));

        Assert.Equal("error", Assert.Single(await AllAsync()).Outcome);
    }

    /// <summary>
    /// Verifies that a cancelled query is recorded as cancelled with zero rows and the cancellation still propagates.
    /// </summary>
    [Fact]
    public async Task ACancelledQuery_IsRecordedAsCancelled_WithZeroRows_AndTheCancellationStillPropagates()
    {
        var requestId = Guid.NewGuid();
        using (AccessLogContext.Begin("ask", requestId))
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => Executor(new FakeExecutor { Throw = new OperationCanceledException() }).ExecuteAsync("contracts", Fetch, Caller.User(_user)));
        }

        var record = Assert.Single(await AllAsync());
        Assert.Equal("cancelled", record.Outcome);
        Assert.Equal(0, record.Rows);
        Assert.Equal("ask", record.Source);
        Assert.Equal(requestId, record.RequestId);
        Assert.Equal(new[] { "contract", "account" }, record.Tables);
    }

    /// <summary>
    /// Verifies that a query that finished before the stop stays recorded next to the cancelled one.
    /// </summary>
    [Fact]
    public async Task AQueryThatFinishedBeforeTheStop_StaysRecorded_NextToTheCancelledOne()
    {
        var caller = Caller.User(_user);
        await Executor(new FakeExecutor { Rows = 2 }).ExecuteAsync("contracts", Fetch, caller);
        await Assert.ThrowsAsync<OperationCanceledException>(() => Executor(new FakeExecutor { Throw = new OperationCanceledException() }).ExecuteAsync("contracts", Fetch, caller));

        var outcomes = (await AllAsync()).Select(r => r.Outcome).OrderBy(o => o).ToList();
        Assert.Equal(new[] { "cancelled", "ok" }, outcomes);
    }

    /// <summary>
    /// Verifies that a cancelled count and a cancelled lookup are recorded as cancelled with their sources.
    /// </summary>
    [Fact]
    public async Task ACancelledCount_AndACancelledLookup_AreRecordedAsCancelled_WithTheirSources()
    {
        var counter = new AccessLoggingRecordCounter(new FakeCounter { Throw = new OperationCanceledException() }, _store, () => Now);
        await Assert.ThrowsAsync<OperationCanceledException>(() => counter.AggregateCountAsync("contracts", Fetch, Caller.User(_user)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => counter.TotalCountAsync("contracts", Fetch, Caller.User(_user)));
        var searcher = new AccessLoggingRecordSearcher(new FakeSearcher { Throw = new OperationCanceledException() }, _store, () => Now);
        var entity = new EntityInfo("account", "accounts", "Account", "accountid", "name", new List<AttributeInfo>(), new List<RelationshipInfo>());
        await Assert.ThrowsAsync<OperationCanceledException>(() => searcher.SearchByNameAsync(entity, "x", Caller.User(_user)));

        var records = await AllAsync();
        Assert.Equal(3, records.Count);
        Assert.All(records, r => Assert.Equal("cancelled", r.Outcome));
        Assert.Equal(2, records.Count(r => r.Source == "count"));
        Assert.Single(records, r => r.Source == "lookup");
    }

    /// <summary>
    /// Verifies that the evaluation caller is not recorded.
    /// </summary>
    [Fact]
    public async Task TheEvaluationCaller_IsNotRecorded()
    {
        await Executor(new FakeExecutor()).ExecuteAsync("contracts", Fetch, Caller.ForEvaluation());

        Assert.Empty(await AllAsync());
    }

    /// <summary>
    /// Verifies that a failing log store never breaks the query.
    /// </summary>
    [Fact]
    public async Task AFailingLogStore_NeverBreaksTheQuery()
    {
        var table = await Executor(new FakeExecutor { Rows = 2 }, new BrokenStore()).ExecuteAsync("contracts", Fetch, Caller.User(_user));

        Assert.Equal(2, table.Rows.Count);
    }

    /// <summary>
    /// Verifies that the record holds no question text fetch XML or filter value.
    /// </summary>
    [Fact]
    public async Task TheRecord_HoldsNoQuestionTextFetchXmlOrFilterValue()
    {
        using (AccessLogContext.Begin("ask"))
        {
            await Executor(new FakeExecutor()).ExecuteAsync("contracts", Fetch, Caller.User(_user));
        }

        var record = Assert.Single(await AllAsync());
        var everything = string.Join("|", record.Source, string.Join(",", record.Tables), record.Outcome);
        Assert.DoesNotContain("Örnekova", everything);
        Assert.DoesNotContain("fetch", everything);
    }

    /// <summary>
    /// Verifies that inside metered ask every query is recorded with the ask source and one shared request ID.
    /// </summary>
    [Fact]
    public async Task InsideMeteredAsk_EveryQueryIsRecordedWithTheAskSource_AndOneSharedRequestId()
    {
        var executor = Executor(new FakeExecutor());
        var metered = new CrmNlQuery.Application.Observability.Metrics.MeteredAsk(async (request, ct) =>
        {
            await executor.ExecuteAsync("contracts", Fetch, request.Caller, ct);
            await executor.ExecuteAsync("accounts", "<fetch><entity name=\"account\" /></fetch>", request.Caller, ct);
            return new CrmNlQuery.Application.Ask.Models.FailureOutcome("x");
        }, new MemoryMetricsStore());

        await metered.RunAsync(new CrmNlQuery.Application.Ask.Models.AskRequest("q", new Dictionary<string, string>(), Caller.User(_user)), default);

        var records = await AllAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal("ask", r.Source));
        Assert.Equal(records[0].RequestId, records[1].RequestId);
        Assert.NotEqual(Guid.Empty, records[0].RequestId);
    }

    private sealed class FakeCounter : CrmNlQuery.Application.Counting.Abstractions.IRecordCounter
    {
        public Exception? Throw;
        public long Aggregate = 621;
        public CrmNlQuery.Application.Counting.Models.RecordCount Total = new CrmNlQuery.Application.Counting.Models.RecordCount(5000, true);

        /// <summary>
        /// Test double that aggregate count with canned data instead of calling a real dependency.
        /// </summary>
        public Task<long> AggregateCountAsync(string entitySetName, string aggregateFetchXml, Caller caller, CancellationToken ct = default)
        {
            if (Throw is not null) { throw Throw; }
            return Task.FromResult(Aggregate);
        }

        /// <summary>
        /// Test double that total count with canned data instead of calling a real dependency.
        /// </summary>
        public Task<CrmNlQuery.Application.Counting.Models.RecordCount> TotalCountAsync(string entitySetName, string totalCountFetchXml, Caller caller, CancellationToken ct = default)
        {
            if (Throw is not null) { throw Throw; }
            return Task.FromResult(Total);
        }
    }

    /// <summary>
    /// Verifies that a count is recorded as the count source with the tables and the counted total and the result is unchanged.
    /// </summary>
    [Fact]
    public async Task ACount_IsRecordedAsTheCountSource_WithTheTablesAndTheCountedTotal_AndTheResultIsUnchanged()
    {
        var counter = new AccessLoggingRecordCounter(new FakeCounter(), _store, () => Now);

        long aggregate;
        CrmNlQuery.Application.Counting.Models.RecordCount total;
        using (AccessLogContext.Begin("result", Guid.NewGuid()))
        {
            aggregate = await counter.AggregateCountAsync("contracts", Fetch, Caller.User(_user));
            total = await counter.TotalCountAsync("contracts", Fetch, Caller.User(_user));
        }

        Assert.Equal(621, aggregate);
        Assert.Equal(5000, total.Total);
        var records = await AllAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, r =>
        {
            Assert.Equal("count", r.Source);
            Assert.Equal(new[] { "contract", "account" }, r.Tables);
            Assert.Equal("ok", r.Outcome);
            Assert.Equal(_user, r.User);
        });
        Assert.Contains(records, r => r.Rows == 621);
        Assert.Contains(records, r => r.Rows == 5000);
    }

    /// <summary>
    /// Verifies that a huge count is clamped to an int a failed count is an error and the evaluation caller is not recorded.
    /// </summary>
    [Fact]
    public async Task AHugeCount_IsClampedToAnInt_AFailedCountIsAnError_AndTheEvaluationCallerIsNotRecorded()
    {
        var huge = new AccessLoggingRecordCounter(new FakeCounter { Aggregate = 9_000_000_000 }, _store, () => Now);
        await huge.AggregateCountAsync("contracts", Fetch, Caller.User(_user));
        Assert.Equal(int.MaxValue, Assert.Single(await AllAsync()).Rows);

        var failing = new AccessLoggingRecordCounter(new FakeCounter { Throw = new CrmException("denied", 403, null, true) }, _store, () => Now);
        await Assert.ThrowsAsync<CrmException>(() => failing.AggregateCountAsync("contracts", Fetch, Caller.User(_user)));
        Assert.Contains(await AllAsync(), r => r.Outcome == "crm_denied" && r.Rows == 0);

        var before = (await AllAsync()).Count;
        await new AccessLoggingRecordCounter(new FakeCounter(), _store, () => Now).AggregateCountAsync("contracts", Fetch, Caller.ForEvaluation());
        Assert.Equal(before, (await AllAsync()).Count);
    }

    /// <summary>
    /// Verifies that a broken store never breaks a count.
    /// </summary>
    [Fact]
    public async Task ABrokenStore_NeverBreaksACount()
    {
        var counter = new AccessLoggingRecordCounter(new FakeCounter(), new BrokenStore(), () => Now);

        Assert.Equal(621, await counter.AggregateCountAsync("contracts", Fetch, Caller.User(_user)));
    }

    /// <summary>
    /// Verifies that a name search is recorded as a lookup with the entity and the match count.
    /// </summary>
    [Fact]
    public async Task ANameSearch_IsRecordedAsALookup_WithTheEntityAndTheMatchCount()
    {
        var searcher = new AccessLoggingRecordSearcher(new FakeSearcher { Matches = 3 }, _store, () => Now);

        using (AccessLogContext.Begin("ask", Guid.NewGuid()))
        {
            await searcher.SearchByNameAsync(TestMetadata.Account(), "Örnekova", Caller.User(_user));
        }

        var record = Assert.Single(await AllAsync());
        Assert.Equal("lookup", record.Source);
        Assert.Equal(new[] { "account" }, record.Tables);
        Assert.Equal(3, record.Rows);
        Assert.Equal("ok", record.Outcome);
    }

    /// <summary>
    /// Verifies that a name search by the evaluation caller or with a broken store is harmless.
    /// </summary>
    [Fact]
    public async Task ANameSearch_ByTheEvaluationCaller_OrWithABrokenStore_IsHarmless()
    {
        var okSearcher = new AccessLoggingRecordSearcher(new FakeSearcher(), _store, () => Now);
        await okSearcher.SearchByNameAsync(TestMetadata.Account(), "x", Caller.ForEvaluation());
        Assert.Empty(await AllAsync());

        var broken = new AccessLoggingRecordSearcher(new FakeSearcher { Matches = 1 }, new BrokenStore(), () => Now);
        Assert.Single(await broken.SearchByNameAsync(TestMetadata.Account(), "x", Caller.User(_user)));
    }

    /// <summary>
    /// Verifies that a failed name search is recorded as an error.
    /// </summary>
    [Fact]
    public async Task AFailedNameSearch_IsRecordedAsAnError()
    {
        var searcher = new AccessLoggingRecordSearcher(new FakeSearcher { Throw = new CrmException("CRM returned 500", 500) }, _store, () => Now);

        await Assert.ThrowsAsync<CrmException>(() => searcher.SearchByNameAsync(TestMetadata.Account(), "x", Caller.User(_user)));

        Assert.Equal("error", Assert.Single(await AllAsync()).Outcome);
    }
}
