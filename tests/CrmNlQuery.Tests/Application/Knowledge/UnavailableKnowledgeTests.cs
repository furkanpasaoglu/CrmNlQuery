using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Knowledge.Models;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Knowledge.Constants;
using CrmNlQuery.Domain.Knowledge.Entities;
using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Tests.Application.Knowledge;

/// <summary>
/// Tests that the service still starts when knowledge base infrastructure such as pgvector is missing, leaving the knowledge base off.
/// </summary>
public class UnavailableKnowledgeTests
{
    private static readonly AccessPrincipal Editor = new AccessPrincipal(@"contoso\editor", "Editor", new[] { "Editor" }, false, new[] { Permissions.DocumentsManage });
    private const string Reason = "the PostgreSQL pgvector extension is not installed";

    private readonly FakeEmbeddings _embeddings = new FakeEmbeddings();
    private readonly List<AuditEntry> _audit = new List<AuditEntry>();

    private KnowledgeService Service()
        => new KnowledgeService(new UnavailableKnowledgeStore(Reason), _embeddings, new KnowledgeOptions { Enabled = true, MinSimilarity = 0.2 },
            (entry, ct) => { _audit.Add(entry); return Task.CompletedTask; });

    /// <summary>
    /// Verifies that the service reports itself unavailable with the reason.
    /// </summary>
    [Fact]
    public void TheService_ReportsItselfUnavailable_WithTheReason()
    {
        var service = Service();

        Assert.False(service.Available);
        Assert.Equal(Reason, service.UnavailableReason);
    }

    /// <summary>
    /// Verifies that an ordinary store is available.
    /// </summary>
    [Fact]
    public void AnOrdinaryStore_IsAvailable()
    {
        var service = new KnowledgeService(new CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge.MemoryKnowledgeStore(), _embeddings, new KnowledgeOptions(), (e, ct) => Task.CompletedTask);

        Assert.True(service.Available);
        Assert.Null(service.UnavailableReason);
    }

    /// <summary>
    /// Verifies that searching returns nothing without calling the embedding model.
    /// </summary>
    [Fact]
    public async Task Searching_ReturnsNothing_WithoutCallingTheEmbeddingModel()
    {
        var service = Service();

        var result = await service.SearchAsync("aktif müşteri nedir");

        Assert.Empty(result);
        Assert.Equal(0, _embeddings.Calls);
    }

    /// <summary>
    /// Verifies that listing is empty and the trace is empty.
    /// </summary>
    [Fact]
    public async Task Listing_IsEmpty_AndTheTraceIsEmpty()
    {
        var service = Service();

        Assert.Empty(await service.ListAsync());
        var trace = await service.TraceAsync("soru", 5);
        Assert.Empty(trace.Fused);
        Assert.Equal(0, _embeddings.Calls);
    }

    /// <summary>
    /// Verifies that writes are refused with a plain message and nothing is audited.
    /// </summary>
    [Fact]
    public async Task Writes_AreRefusedWithAPlainMessage_AndNothingIsAudited()
    {
        var service = Service();

        var create = await service.CreateAsync(Editor, "Başlık", "Metin");
        var update = await service.UpdateAsync(Editor, Guid.NewGuid(), "Başlık", "Metin");
        var enable = await service.SetEnabledAsync(Editor, Guid.NewGuid(), true);
        var delete = await service.DeleteAsync(Editor, Guid.NewGuid());

        foreach (var result in new[] { create, update, enable, delete })
        {
            Assert.False(result.Ok);
            Assert.Equal(AccessError.Unavailable, result.Error);
            Assert.Contains("not available", result.Message);
            Assert.Contains(Reason, result.Message);
        }
        Assert.Empty(_audit);
        Assert.Equal(0, _embeddings.Calls);
    }

    /// <summary>
    /// Verifies that the store itself refuses writes and answers reads empty.
    /// </summary>
    [Fact]
    public async Task TheStoreItself_RefusesWrites_AndAnswersReadsEmpty()
    {
        var store = new UnavailableKnowledgeStore(Reason);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(
            new KnowledgeDocument(Guid.NewGuid(), "t", "b", true, "x", DateTimeOffset.UtcNow), new List<KnowledgeChunk>()));
        Assert.Empty(await store.SearchByVectorAsync(new float[KnowledgeLimits.EmbeddingDimensions], 5));
        Assert.Empty(await store.SearchByTextAsync("soru", 5));
        Assert.Null(await store.GetAsync(Guid.NewGuid()));
    }
}
