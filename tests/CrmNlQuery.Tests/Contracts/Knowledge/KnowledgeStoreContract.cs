using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Domain.Knowledge.Entities;

namespace CrmNlQuery.Tests.Contracts.Knowledge;

/// <summary>
/// Shared behaviours every knowledge store implementation (memory, PostgreSQL) must pass.
/// </summary>
public abstract class KnowledgeStoreContract : IAsyncLifetime
{
    private readonly FakeEmbeddings _embeddings = new FakeEmbeddings();
    private readonly List<Guid> _created = new List<Guid>();

    protected abstract IKnowledgeStore CreateStore();

    /// <summary>
    /// Prepares the store for each test.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Cleans up the store after each test.
    /// </summary>
    public async Task DisposeAsync()
    {
        var store = CreateStore();
        foreach (var id in _created)
        {
            await store.DeleteAsync(id);
        }
    }

    private static string Marker() => "zq" + Guid.NewGuid().ToString("N").Substring(0, 12);

    private async Task<KnowledgeDocument> Save(IKnowledgeStore store, string title, params string[] chunkTexts)
    {
        var document = new KnowledgeDocument(Guid.NewGuid(), title, string.Join("\n\n", chunkTexts), true, @"CONTOSO\editor", DateTimeOffset.UtcNow);
        _created.Add(document.Id);
        var chunks = chunkTexts.Select((t, i) => new KnowledgeChunk(i, t, _embeddings.Embed(t))).ToList();
        await store.SaveAsync(document, chunks);
        return document;
    }

    /// <summary>
    /// Verifies that a document round trips and is listed.
    /// </summary>
    [Fact]
    public async Task ADocument_RoundTrips_AndIsListed()
    {
        var store = CreateStore();
        var marker = Marker();
        var saved = await Save(store, "Aktif müşteri " + marker, "Son 12 ayda siparişi olan hesap " + marker);

        var loaded = await store.GetAsync(saved.Id);

        Assert.NotNull(loaded);
        Assert.Equal(saved.Title, loaded!.Title);
        Assert.Equal(saved.Body, loaded.Body);
        Assert.True(loaded.Enabled);
        Assert.Equal(@"CONTOSO\editor", loaded.UpdatedBy);
        Assert.Contains(await store.ListAsync(), d => d.Id == saved.Id);
    }

    /// <summary>
    /// Verifies that a missing document is null.
    /// </summary>
    [Fact]
    public async Task AMissingDocument_IsNull()
    {
        Assert.Null(await CreateStore().GetAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Verifies that saving again replaces the chunks.
    /// </summary>
    [Fact]
    public async Task SavingAgain_ReplacesTheChunks()
    {
        var store = CreateStore();
        var marker = Marker();
        var first = await Save(store, "Belge " + marker, "eskiicerik" + marker);
        var again = first with { Body = "yeniicerik" + marker };
        await store.SaveAsync(again, new[] { new KnowledgeChunk(0, "yeniicerik" + marker, _embeddings.Embed("yeniicerik" + marker)) });

        var oldHits = await store.SearchByTextAsync("eskiicerik" + marker, 10);
        var newHits = await store.SearchByTextAsync("yeniicerik" + marker, 10);

        Assert.DoesNotContain(oldHits, s => s.DocumentId == first.Id);
        Assert.Contains(newHits, s => s.DocumentId == first.Id);
    }

    /// <summary>
    /// Verifies that vector search puts the closest chunk first and scores identical text near one.
    /// </summary>
    [Fact]
    public async Task VectorSearch_PutsTheClosestChunkFirst_AndScoresIdenticalTextNearOne()
    {
        var store = CreateStore();
        var marker = Marker();
        var near = await Save(store, "Yakın " + marker, "kobi musteri segmenti " + marker);
        var far = await Save(store, "Uzak " + marker, "tamamen alakasiz baska konu " + marker + "x");

        var hits = (await store.SearchByVectorAsync(_embeddings.Embed("kobi musteri segmenti " + marker), 50))
            .Where(s => s.DocumentId == near.Id || s.DocumentId == far.Id)
            .ToList();

        Assert.Equal(near.Id, hits[0].DocumentId);
        Assert.True(hits[0].Score > 0.99);
        Assert.True(hits[0].Score > hits[1].Score);
    }

    /// <summary>
    /// Verifies that text search finds an exact code name ignoring case.
    /// </summary>
    [Fact]
    public async Task TextSearch_FindsAnExactCodeName_IgnoringCase()
    {
        var store = CreateStore();
        var marker = Marker();
        var doc = await Save(store, "Alan " + marker, "Müşteri segmenti için new_musteriSegmenti" + marker + " alanı kullanılır");

        var hits = await store.SearchByTextAsync("new_musterisegmenti" + marker + " nedir", 10);

        Assert.Contains(hits, s => s.DocumentId == doc.Id);
    }

    /// <summary>
    /// Verifies that a disabled document is not found by either search until enabled again.
    /// </summary>
    [Fact]
    public async Task ADisabledDocument_IsNotFound_ByEitherSearch_UntilEnabledAgain()
    {
        var store = CreateStore();
        var marker = Marker();
        var doc = await Save(store, "Pasif " + marker, "gizli kural " + marker);
        await store.SetEnabledAsync(doc.Id, false, @"CONTOSO\editor", DateTimeOffset.UtcNow);

        Assert.DoesNotContain(await store.SearchByTextAsync(marker, 10), s => s.DocumentId == doc.Id);
        Assert.DoesNotContain(await store.SearchByVectorAsync(_embeddings.Embed("gizli kural " + marker), 50), s => s.DocumentId == doc.Id);
        Assert.False((await store.GetAsync(doc.Id))!.Enabled);

        await store.SetEnabledAsync(doc.Id, true, @"CONTOSO\editor", DateTimeOffset.UtcNow);

        Assert.Contains(await store.SearchByTextAsync(marker, 10), s => s.DocumentId == doc.Id);
    }

    /// <summary>
    /// Verifies that a deleted document is gone everywhere.
    /// </summary>
    [Fact]
    public async Task ADeletedDocument_IsGoneEverywhere()
    {
        var store = CreateStore();
        var marker = Marker();
        var doc = await Save(store, "Silinecek " + marker, "silinecek icerik " + marker);

        await store.DeleteAsync(doc.Id);

        Assert.Null(await store.GetAsync(doc.Id));
        Assert.DoesNotContain(await store.ListAsync(), d => d.Id == doc.Id);
        Assert.DoesNotContain(await store.SearchByTextAsync(marker, 10), s => s.DocumentId == doc.Id);
    }

    /// <summary>
    /// Verifies that a snippet carries its document title and chunk number.
    /// </summary>
    [Fact]
    public async Task ASnippet_CarriesItsDocumentTitleAndChunkNumber()
    {
        var store = CreateStore();
        var marker = Marker();
        var doc = await Save(store, "Başlık " + marker, "ilk parca", "ikinci parca " + marker);

        var hit = (await store.SearchByTextAsync("ikinci parca " + marker, 10)).First(s => s.DocumentId == doc.Id);

        Assert.Equal("Başlık " + marker, hit.Title);
        Assert.Equal(1, hit.ChunkIndex);
        Assert.Contains("ikinci parca", hit.Text);
    }

    /// <summary>
    /// Verifies that SQL-looking search input is treated as plain text.
    /// </summary>
    [Fact]
    public async Task SearchText_WithSqlLookingInput_IsPlainText()
    {
        var store = CreateStore();

        var hits = await store.SearchByTextAsync("x'); DROP TABLE knowledge_documents; --", 10);

        Assert.NotNull(hits);
        Assert.NotNull(await store.ListAsync());
    }
}
