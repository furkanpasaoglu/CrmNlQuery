using CrmNlQuery.Tests.Contracts.Knowledge;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Domain.Knowledge.Entities;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Knowledge;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.Knowledge;

/// <summary>
/// Tests the PostgreSQL knowledge store against a real database with the same contract as the in-memory store.
/// </summary>
[Trait("Category", "Live")]
public class PostgresKnowledgeStoreTests : KnowledgeStoreContract
{
    protected override IKnowledgeStore CreateStore()
    {
        return PostgresKnowledgeStore.CreateAsync(PostgresHistoryStoreTests.ConnectionString).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Verifies that turkish endings are stripped so an inflected word still matches.
    /// </summary>
    [Fact]
    public async Task TurkishEndings_AreStripped_SoAnInflectedWordStillMatches()
    {
        var store = CreateStore();
        var marker = "zq" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var embeddings = new FakeEmbeddings();
        var document = new KnowledgeDocument(Guid.NewGuid(), "Müşteri " + marker, "müşteri kuralları " + marker, true, @"CONTOSO\editor", DateTimeOffset.UtcNow);
        await store.SaveAsync(document, new[] { new KnowledgeChunk(0, "müşteri kuralları " + marker, embeddings.Embed("müşteri kuralları")) });
        try
        {
            var hits = await store.SearchByTextAsync("müşterilerimizin kuralı " + marker, 20);

            Assert.Contains(hits, s => s.DocumentId == document.Id);
        }
        finally
        {
            await store.DeleteAsync(document.Id);
        }
    }

    /// <summary>
    /// Verifies that or query keeps words only and joins them with or.
    /// </summary>
    [Theory]
    [InlineData("KOBİ müşterileri nedir?", "KOBİ or müşterileri")]
    [InlineData("a b or c", "")]
    [InlineData("new_musteriSegmenti alanı", "new_musteriSegmenti or alanı")]
    [InlineData("x'); DROP TABLE t; --", "DROP or TABLE")]
    public void OrQuery_KeepsWordsOnly_AndJoinsThemWithOr(string question, string expected)
    {
        Assert.Equal(expected, PostgresKnowledgeStore.OrQuery(question));
    }
}
