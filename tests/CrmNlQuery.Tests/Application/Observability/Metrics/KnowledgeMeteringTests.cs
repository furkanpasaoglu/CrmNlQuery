using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Knowledge.Models;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Application.Observability.Metrics;

/// <summary>
/// Tests that the number of company-knowledge pieces added to a request is collected per request and recorded with it.
/// </summary>
public class KnowledgeMeteringTests
{
    private static readonly AccessPrincipal Editor = new AccessPrincipal(@"contoso\editor", "Editor", new[] { "Editor" }, false, new[] { "Documents.Manage" });
    private static readonly AskRequest Request = new AskRequest("q", new Dictionary<string, string>(), Caller.User(Guid.NewGuid()));

    private sealed class Spy : IMetricsStore
    {
        public List<RequestRecord> Requests { get; } = new List<RequestRecord>();
        /// <summary>
        /// Captures the AI call record for assertions.
        /// </summary>
        public Task RecordAiCallAsync(AiCallRecord record, CancellationToken ct = default) => Task.CompletedTask;
        /// <summary>
        /// Captures the request record so tests can inspect the recorded knowledge piece count.
        /// </summary>
        public Task RecordRequestAsync(RequestRecord record, CancellationToken ct = default) { Requests.Add(record); return Task.CompletedTask; }
        /// <summary>
        /// Test spy metrics store that returns an empty summary, since these knowledge metering tests only check what was recorded.
        /// </summary>
        public Task<MetricsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>
        /// Does nothing in this test spy.
        /// </summary>
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static AnswerOutcome Answer()
        => new AnswerOutcome("account", "u", "<fetch/>", new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>()), false);

    /// <summary>
    /// Verifies that outside a request recording is a no op and nothing is reported.
    /// </summary>
    [Fact]
    public void OutsideARequest_RecordingIsANoOp_AndNothingIsReported()
    {
        RequestContext.RecordKnowledgeSearch(3);

        Assert.Null(RequestContext.KnowledgePieces);
    }

    /// <summary>
    /// Verifies that inside a request no search means null a search with nothing means zero and searches add.
    /// </summary>
    [Fact]
    public void InsideARequest_NoSearchMeansNull_ASearchWithNothingMeansZero_AndSearchesAdd()
    {
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            Assert.Null(RequestContext.KnowledgePieces);
            RequestContext.RecordKnowledgeSearch(0);
            Assert.Equal(0, RequestContext.KnowledgePieces);
            RequestContext.RecordKnowledgeSearch(3);
            RequestContext.RecordKnowledgeSearch(2);
            Assert.Equal(5, RequestContext.KnowledgePieces);
        }
        Assert.Null(RequestContext.KnowledgePieces);
    }

    /// <summary>
    /// Verifies that a nested request has its own counter and the outer one is restored.
    /// </summary>
    [Fact]
    public void ANestedRequest_HasItsOwnCounter_AndTheOuterOneIsRestored()
    {
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            RequestContext.RecordKnowledgeSearch(4);
            using (RequestContext.Begin(Guid.NewGuid()))
            {
                Assert.Null(RequestContext.KnowledgePieces);
                RequestContext.RecordKnowledgeSearch(1);
            }
            Assert.Equal(4, RequestContext.KnowledgePieces);
        }
    }

    /// <summary>
    /// Verifies that counts recorded in child tasks reach the parent because the counter is shared.
    /// </summary>
    [Fact]
    public async Task CountsRecordedInChildTasks_ReachTheParent_BecauseTheCounterIsShared()
    {
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => RequestContext.RecordKnowledgeSearch(1))));

            Assert.Equal(20, RequestContext.KnowledgePieces);
        }
    }

    /// <summary>
    /// Verifies that a question that never searched the knowledge base is recorded with null.
    /// </summary>
    [Fact]
    public async Task AQuestionThatNeverSearchedTheKnowledgeBase_IsRecordedWithNull()
    {
        var spy = new Spy();

        await new MeteredAsk((r, ct) => Task.FromResult<AskOutcome>(Answer()), spy).RunAsync(Request, default);

        Assert.Null(Assert.Single(spy.Requests).KnowledgePieces);
    }

    /// <summary>
    /// Verifies that the recorded request carries the pieces added while it ran.
    /// </summary>
    [Fact]
    public async Task TheRecordedRequest_CarriesThePiecesAddedWhileItRan()
    {
        var spy = new Spy();
        var metered = new MeteredAsk((r, ct) =>
        {
            RequestContext.RecordKnowledgeSearch(3);
            return Task.FromResult<AskOutcome>(Answer());
        }, spy);

        await metered.RunAsync(Request, default);

        Assert.Equal(3, Assert.Single(spy.Requests).KnowledgePieces);
    }

    /// <summary>
    /// Verifies that a search that found nothing is recorded as zero not null.
    /// </summary>
    [Fact]
    public async Task ASearchThatFoundNothing_IsRecordedAsZero_NotNull()
    {
        var spy = new Spy();
        var metered = new MeteredAsk((r, ct) =>
        {
            RequestContext.RecordKnowledgeSearch(0);
            return Task.FromResult<AskOutcome>(Answer());
        }, spy);

        await metered.RunAsync(Request, default);

        Assert.Equal(0, Assert.Single(spy.Requests).KnowledgePieces);
    }

    /// <summary>
    /// Verifies that a second question does not inherit the first ones count.
    /// </summary>
    [Fact]
    public async Task ASecondQuestionDoesNotInheritTheFirstOnesCount()
    {
        var spy = new Spy();
        var first = true;
        var metered = new MeteredAsk((r, ct) =>
        {
            if (first) { RequestContext.RecordKnowledgeSearch(5); first = false; }
            return Task.FromResult<AskOutcome>(Answer());
        }, spy);

        await metered.RunAsync(Request, default);
        await metered.RunAsync(Request, default);

        Assert.Equal(new int?[] { 5, null }, spy.Requests.Select(r => r.KnowledgePieces));
    }

    private static async Task<KnowledgeRetriever> RetrieverWithDocument(bool enabled, FakeEmbeddings? embeddings = null)
    {
        var service = new KnowledgeService(new MemoryKnowledgeStore(), embeddings ?? new FakeEmbeddings(), new KnowledgeOptions { MinSimilarity = 0.2 }, (e, c) => Task.CompletedTask);
        await service.CreateAsync(Editor, "KOBİ", "kobi müşteri segmenti iki");
        return new KnowledgeRetriever(service, enabled, NullLogger.Instance);
    }

    /// <summary>
    /// Verifies that the retriever reports how many pieces it added to the request.
    /// </summary>
    [Fact]
    public async Task TheRetriever_ReportsHowManyPiecesItAdded_ToTheRequest()
    {
        var retriever = await RetrieverWithDocument(true);
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            var notes = await retriever.FindAsync("kobi müşteri segmenti");

            Assert.Equal(notes.Count, RequestContext.KnowledgePieces);
            Assert.True(notes.Count > 0);
        }
    }

    /// <summary>
    /// Verifies that when the knowledge base is off nothing is reported so the dashboard can tell off from no match.
    /// </summary>
    [Fact]
    public async Task WhenTheKnowledgeBaseIsOff_NothingIsReported_SoTheDashboardCanTellOffFromNoMatch()
    {
        var retriever = await RetrieverWithDocument(false);
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            await retriever.FindAsync("kobi müşteri segmenti");

            Assert.Null(RequestContext.KnowledgePieces);
        }
    }

    /// <summary>
    /// Verifies that when the search fails nothing is reported and the question continues.
    /// </summary>
    [Fact]
    public async Task WhenTheSearchFails_NothingIsReported_AndTheQuestionContinues()
    {
        var embeddings = new FakeEmbeddings();
        var retriever = await RetrieverWithDocument(true, embeddings);
        embeddings.Down = true;
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            var notes = await retriever.FindAsync("kobi müşteri segmenti");

            Assert.Empty(notes);
            Assert.Null(RequestContext.KnowledgePieces);
        }
    }

    /// <summary>
    /// Verifies that a follow up answer is recorded as follow up a new answer as not and questions and failures as unknown.
    /// </summary>
    [Fact]
    public async Task AFollowUpAnswer_IsRecordedAsFollowUp_ANewAnswerAsNot_AndQuestionsAndFailuresAsUnknown()
    {
        var spy = new Spy();
        AskOutcome next = Answer() with { BasedOn = "previous question" };
        var metered = new MeteredAsk((r, ct) => Task.FromResult(next), spy);

        await metered.RunAsync(Request, default);
        next = Answer();
        await metered.RunAsync(Request, default);
        next = new QuestionOutcome("columns", "columns", "Which columns?", new List<Choice>(), false);
        await metered.RunAsync(Request, default);
        next = new FailureOutcome("x", "other");
        await metered.RunAsync(Request, default);

        Assert.Equal(new bool?[] { true, false, null, null }, spy.Requests.Select(r => r.FollowUp));
    }
}
