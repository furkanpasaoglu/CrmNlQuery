using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Knowledge.Models;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Knowledge.Constants;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Knowledge.Services;

/// <summary>
/// Tests the knowledge service: document validation, indexing, auditing and search behavior.
/// </summary>
public class KnowledgeServiceTests
{
    private static readonly AccessPrincipal Editor = new AccessPrincipal(@"contoso\editor", "Editor", new[] { "Editor" }, false, new[] { Permissions.DocumentsManage });

    private readonly MemoryKnowledgeStore _store = new MemoryKnowledgeStore();
    private readonly FakeEmbeddings _embeddings = new FakeEmbeddings();
    private readonly List<AuditEntry> _audit = new List<AuditEntry>();

    private KnowledgeService Service(KnowledgeOptions? options = null)
        => new KnowledgeService(_store, _embeddings, options ?? new KnowledgeOptions { MinSimilarity = 0.2 },
            (entry, ct) => { _audit.Add(entry); return Task.CompletedTask; });

    /// <summary>
    /// Verifies that a duplicate title is rejected regardless of case, spacing and punctuation.
    /// </summary>
    [Theory]
    [InlineData("Aktif müşteri")]
    [InlineData("  AKTİF   MÜŞTERİ ")]
    [InlineData("aktif musteri.")]
    public async Task Create_RejectsASecondDocumentWithTheSameTitle_IgnoringCaseTurkishLettersAndPunctuation(string title)
    {
        var service = Service();
        Assert.True((await service.CreateAsync(Editor, "Aktif müşteri", "Son 12 ayda siparişi olan hesap.")).Ok);

        var second = await service.CreateAsync(Editor, title, "Tamamen başka bir metin");

        Assert.Equal(AccessError.Conflict, second.Error);
        Assert.Single(await _store.ListAsync());
    }

    /// <summary>
    /// Verifies that creating a document with the same text under another title is rejected.
    /// </summary>
    [Fact]
    public async Task Create_RejectsTheSameText_UnderAnotherTitle()
    {
        var service = Service();
        Assert.True((await service.CreateAsync(Editor, "KOBİ", "Çalışan sayısı 250'den az olan hesap.")).Ok);

        var second = await service.CreateAsync(Editor, "Küçük işletme", "çalışan SAYISI 250 den az olan hesap");

        Assert.Equal(AccessError.Conflict, second.Error);
        Assert.Contains("KOBİ", second.Message);
    }

    /// <summary>
    /// Verifies that update may keep its own title but not take another document's title.
    /// </summary>
    [Fact]
    public async Task Update_AllowsKeepingItsOwnTitle_ButNotTakingAnotherDocumentsTitle()
    {
        var service = Service();
        var a = (await service.CreateAsync(Editor, "Birinci", "birinci metin")).Id!.Value;
        await service.CreateAsync(Editor, "İkinci", "ikinci metin");

        Assert.True((await service.UpdateAsync(Editor, a, "Birinci", "birinci metin güncel")).Ok);
        Assert.Equal(AccessError.Conflict, (await service.UpdateAsync(Editor, a, "ikinci", "birinci metin güncel")).Error);
    }

    /// <summary>
    /// Verifies that create saves the trimmed document, indexes it and audits without the text.
    /// </summary>
    [Fact]
    public async Task Create_SavesTrimmedDocument_Indexes_AndAudits_WithoutTheText()
    {
        var service = Service();

        var result = await service.CreateAsync(Editor, "  Aktif müşteri ", " Son 12 ayda siparişi olan hesap. ");

        Assert.True(result.Ok);
        var saved = await _store.GetAsync(result.Id!.Value);
        Assert.Equal("Aktif müşteri", saved!.Title);
        Assert.Equal("Son 12 ayda siparişi olan hesap.", saved.Body);
        Assert.Equal(@"contoso\editor", saved.UpdatedBy);
        var entry = Assert.Single(_audit);
        Assert.Equal("doc.create", entry.Action);
        Assert.Equal("Aktif müşteri", entry.Target);
        Assert.DoesNotContain("siparişi", entry.Detail);
    }

    /// <summary>
    /// Verifies that blank titles or texts are rejected.
    /// </summary>
    [Theory]
    [InlineData("", "metin")]
    [InlineData("   ", "metin")]
    [InlineData("başlık", "")]
    [InlineData("başlık", "  \n ")]
    public async Task Create_RejectsAnEmptyTitleOrText(string title, string body)
    {
        var result = await Service().CreateAsync(Editor, title, body);

        Assert.Equal(AccessError.Invalid, result.Error);
        Assert.Empty(await _store.ListAsync());
        Assert.Empty(_audit);
    }

    /// <summary>
    /// Verifies that a too long title or text is rejected on create.
    /// </summary>
    [Fact]
    public async Task Create_RejectsTooLongTitleOrText()
    {
        var service = Service();

        Assert.Equal(AccessError.Invalid, (await service.CreateAsync(Editor, new string('t', KnowledgeLimits.MaxTitleLength + 1), "x")).Error);
        Assert.Equal(AccessError.Invalid, (await service.CreateAsync(Editor, "t", new string('x', KnowledgeLimits.MaxBodyLength + 1))).Error);
    }

    /// <summary>
    /// Verifies that create is refused and nothing saved when the embedding model is down.
    /// </summary>
    [Fact]
    public async Task Create_WhenTheEmbeddingModelIsDown_IsRefused_AndNothingIsSaved()
    {
        _embeddings.Down = true;

        var result = await Service().CreateAsync(Editor, "Başlık", "Metin");

        Assert.Equal(AccessError.Unavailable, result.Error);
        Assert.Contains("embedding model", result.Message);
        Assert.Empty(await _store.ListAsync());
        Assert.Empty(_audit);
    }

    /// <summary>
    /// Verifies that a wrong vector size is refused with a clear message.
    /// </summary>
    [Fact]
    public async Task Create_WithTheWrongVectorSize_IsRefusedWithAClearMessage()
    {
        _embeddings.Dimensions = 768;

        var result = await Service().CreateAsync(Editor, "Başlık", "Metin");

        Assert.Equal(AccessError.Unavailable, result.Error);
        Assert.Contains("1024", result.Message);
        Assert.Empty(await _store.ListAsync());
    }

    /// <summary>
    /// Verifies that update reindexes the new text, keeps the enabled state and audits.
    /// </summary>
    [Fact]
    public async Task Update_ReindexesTheNewText_KeepsEnabledState_AndAudits()
    {
        var service = Service();
        var id = (await service.CreateAsync(Editor, "Terim", "eskiicerik burada")).Id!.Value;
        await service.SetEnabledAsync(Editor, id, false);

        var result = await service.UpdateAsync(Editor, id, "Terim", "yeniicerik burada");

        Assert.True(result.Ok);
        var doc = (await _store.GetAsync(id))!;
        Assert.Equal("yeniicerik burada", doc.Body);
        Assert.False(doc.Enabled);
        Assert.Equal("doc.update", _audit.Last().Action);
        await service.SetEnabledAsync(Editor, id, true);
        Assert.Empty(await _store.SearchByTextAsync("eskiicerik", 5));
        Assert.NotEmpty(await _store.SearchByTextAsync("yeniicerik", 5));
    }

    /// <summary>
    /// Verifies that update keeps the old version when embedding fails.
    /// </summary>
    [Fact]
    public async Task Update_WhenEmbeddingFails_KeepsTheOldVersion()
    {
        var service = Service();
        var id = (await service.CreateAsync(Editor, "Terim", "eskiicerik")).Id!.Value;
        _embeddings.Down = true;

        var result = await service.UpdateAsync(Editor, id, "Terim", "yeniicerik");

        Assert.Equal(AccessError.Unavailable, result.Error);
        Assert.Equal("eskiicerik", (await _store.GetAsync(id))!.Body);
    }

    /// <summary>
    /// Verifies that update, enable and delete of a missing document return not found.
    /// </summary>
    [Fact]
    public async Task UpdateEnableAndDelete_OfAMissingDocument_AreNotFound()
    {
        var service = Service();
        var id = Guid.NewGuid();

        Assert.Equal(AccessError.NotFound, (await service.UpdateAsync(Editor, id, "t", "b")).Error);
        Assert.Equal(AccessError.NotFound, (await service.SetEnabledAsync(Editor, id, false)).Error);
        Assert.Equal(AccessError.NotFound, (await service.DeleteAsync(Editor, id)).Error);
    }

    /// <summary>
    /// Verifies that delete removes the document and audits.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheDocument_AndAudits()
    {
        var service = Service();
        var id = (await service.CreateAsync(Editor, "Terim", "metin")).Id!.Value;

        Assert.True((await service.DeleteAsync(Editor, id)).Ok);

        Assert.Null(await _store.GetAsync(id));
        Assert.Equal("doc.delete", _audit.Last().Action);
    }

    /// <summary>
    /// Verifies that disabling stops the document from being found.
    /// </summary>
    [Fact]
    public async Task Disabling_StopsTheDocumentFromBeingFound()
    {
        var service = Service();
        var id = (await service.CreateAsync(Editor, "KOBİ", "kobi müşteri segmenti iki")).Id!.Value;
        Assert.NotEmpty(await service.SearchAsync("kobi segmenti"));

        await service.SetEnabledAsync(Editor, id, false);

        Assert.Empty(await service.SearchAsync("kobi segmenti"));
        Assert.Equal("doc.disable", _audit.Last().Action);
    }

    /// <summary>
    /// Verifies that search finds the relevant document and says how it was found.
    /// </summary>
    [Fact]
    public async Task Search_FindsTheRelevantDocument_AndSaysHowItWasFound()
    {
        var service = Service();
        await service.CreateAsync(Editor, "KOBİ", "KOBİ müşteri segmenti iki değeridir");
        await service.CreateAsync(Editor, "Fatura", "fatura vade tarihi ödeme koşulları");

        var hits = await service.SearchAsync("kobi müşteri segmenti nedir");

        Assert.Equal("KOBİ", hits[0].Title);
        Assert.Equal("both", hits[0].Source);
        Assert.True(hits[0].Score > 0);
    }

    /// <summary>
    /// Verifies that search drops vector matches below the similarity floor.
    /// </summary>
    [Fact]
    public async Task Search_DropsVectorMatchesBelowTheSimilarityFloor()
    {
        var service = Service(new KnowledgeOptions { MinSimilarity = 0.99 });
        await service.CreateAsync(Editor, "Fatura", "fatura vade tarihi ödeme koşulları");

        Assert.Empty(await service.SearchAsync("zebra ağaç gökyüzü"));
    }

    /// <summary>
    /// Verifies that search respects the top-K and character budget but always keeps the first hit.
    /// </summary>
    [Fact]
    public async Task Search_RespectsTopK_AndTheCharacterBudget_ButAlwaysKeepsTheFirstHit()
    {
        var service = Service(new KnowledgeOptions { MinSimilarity = 0, TopK = 2, MaxChars = 10 });
        for (var i = 0; i < 4; i++)
        {
            await service.CreateAsync(Editor, "Belge " + i, "ortak kelime bilgi " + i);
        }

        var hits = await service.SearchAsync("ortak kelime bilgi");

        Assert.Single(hits);
    }

    /// <summary>
    /// Verifies that searching a blank question returns nothing without calling the model.
    /// </summary>
    [Fact]
    public async Task Search_OfABlankQuestion_ReturnsNothing_WithoutCallingTheModel()
    {
        var service = Service();

        Assert.Empty(await service.SearchAsync("  "));
        Assert.Equal(0, _embeddings.Calls);
    }

    /// <summary>
    /// Verifies that search throws when the embedding model is down so the caller can decide.
    /// </summary>
    [Fact]
    public async Task Search_WhenTheEmbeddingModelIsDown_Throws_SoTheCallerCanDecide()
    {
        var service = Service();
        await service.CreateAsync(Editor, "Terim", "metin");
        _embeddings.Down = true;

        await Assert.ThrowsAsync<LlmUnavailableException>(() => service.SearchAsync("terim"));
    }
}
