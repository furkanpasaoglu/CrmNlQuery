using CrmNlQuery.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Application.Knowledge.Models;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Application.Knowledge.Services;

/// <summary>
/// Unit tests for the knowledge retriever combining embeddings, similarity threshold and keyword search.
/// </summary>
public class KnowledgeRetrieverTests
{
    private readonly MemoryKnowledgeStore _store = new MemoryKnowledgeStore();
    private readonly FakeEmbeddings _embeddings = new FakeEmbeddings();

    private KnowledgeService Service() => new KnowledgeService(_store, _embeddings, new KnowledgeOptions { MinSimilarity = 0.2 },
        (entry, ct) => Task.CompletedTask);

    private static readonly CrmNlQuery.Domain.Access.Entities.AccessPrincipal Editor =
        new CrmNlQuery.Domain.Access.Entities.AccessPrincipal(@"contoso\editor", "Editor", new[] { "Editor" }, false, new[] { "Documents.Manage" });

    /// <summary>
    /// Verifies that when off it never searches not even the embedding model.
    /// </summary>
    [Fact]
    public async Task WhenOff_ItNeverSearches_NotEvenTheEmbeddingModel()
    {
        var service = Service();
        await service.CreateAsync(Editor, "KOBİ", "kobi müşteri segmenti iki");
        var calls = _embeddings.Calls;

        var found = await new KnowledgeRetriever(service, false, NullLogger.Instance).FindAsync("kobi müşteri segmenti");

        Assert.Empty(found);
        Assert.Equal(calls, _embeddings.Calls);
    }

    /// <summary>
    /// Verifies that when on it returns the text of the relevant pieces.
    /// </summary>
    [Fact]
    public async Task WhenOn_ItReturnsTheTextOfTheRelevantPieces()
    {
        var service = Service();
        await service.CreateAsync(Editor, "KOBİ", "kobi müşteri segmenti iki");

        var found = await new KnowledgeRetriever(service, true, NullLogger.Instance).FindAsync("kobi müşteri segmenti nedir");

        Assert.Single(found);
        Assert.Contains("kobi müşteri segmenti iki", found[0]);
        Assert.StartsWith("KOBİ", found[0]);
    }

    /// <summary>
    /// Verifies that when the embedding model is down the question continues without knowledge and a warning is logged.
    /// </summary>
    [Fact]
    public async Task WhenTheEmbeddingModelIsDown_TheQuestionContinuesWithoutKnowledge_AndAWarningIsLogged()
    {
        var service = Service();
        await service.CreateAsync(Editor, "KOBİ", "kobi müşteri segmenti iki");
        _embeddings.Down = true;
        var logger = new ListLogger();

        var found = await new KnowledgeRetriever(service, true, logger).FindAsync("kobi müşteri segmenti");

        Assert.Empty(found);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain("kobi", warning.Message);
    }

    /// <summary>
    /// Verifies that a cancelled search is not swallowed.
    /// </summary>
    [Fact]
    public async Task ACancelledSearch_IsNotSwallowed()
    {
        var service = Service();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var embeddings = new CancellingEmbeddings();
        var cancelling = new KnowledgeService(_store, embeddings, new KnowledgeOptions(), (e, c) => Task.CompletedTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new KnowledgeRetriever(cancelling, true, NullLogger.Instance).FindAsync("soru", cts.Token));
    }

    private sealed class CancellingEmbeddings : IEmbeddingClient
    {
        /// <summary>
        /// Test double embedding client that throws a cancellation when asked to embed, checking that cancellation propagates.
        /// </summary>
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<float[]>>(new List<float[]>());
        }
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new List<(LogLevel, string)>();
        /// <summary>
        /// Test logger that records log calls: begin scope.
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>
        /// Test logger that records log calls: is enabled.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>
        /// Test logger that records log calls: log.
        /// </summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
