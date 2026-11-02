using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Glossary.Models;
using CrmNlQuery.Application.Glossary.Services;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Application.Glossary;

/// <summary>
/// Tests that equivalent glossary terms such as talep and Talepler are refused as duplicates while containing terms are not equivalent.
/// </summary>
public class GlossaryEquivalentTermTests
{
    /// <summary>
    /// Verifies that equivalent terms are recognized and containing terms are not.
    /// </summary>
    [Theory]
    [InlineData("talep", "Talepler", true)]
    [InlineData("talep", "taleplerimiz", true)]
    [InlineData("large company", "large companies", true)]
    [InlineData("KOBİ", "kobi'ler", true)]
    [InlineData("büyük", "büyük firma", false)]
    [InlineData("açık fırsat", "açık fırsatlar", true)]
    [InlineData("açık fırsat", "kapalı fırsat", false)]
    [InlineData("talep", "talepname", false)]
    public void Equivalent_MeansTheSameWordsUpToEndings(string a, string b, bool expected)
    {
        Assert.Equal(expected, GlossaryMatcher.Equivalent(a, b));
        Assert.Equal(expected, GlossaryMatcher.Equivalent(b, a));
    }

    private static GlossaryEntry Alias(string term) => new GlossaryEntry { Kind = GlossaryKind.Alias, Entity = "account", Terms = new[] { new GlossaryTerm(term) } };

    /// <summary>
    /// Verifies that an equivalent term is refused as a duplicate on add and update.
    /// </summary>
    [Fact]
    public async Task AnEquivalentTerm_IsRefusedAsADuplicate_OnAddAndOnUpdate()
    {
        var store = new MemoryGlossaryStore();
        var service = new GlossaryService(store, TestMetadata.Create(), () => true);
        var first = await service.AddAsync(Alias("açık fırsat"), "boss");

        var plural = await service.AddAsync(Alias("Açık fırsatlar"), "boss");
        var other = await service.AddAsync(Alias("potansiyel firma"), "boss");
        var update = await service.UpdateAsync((await store.GetAsync(other.EntryId!))! with { Terms = new[] { new GlossaryTerm("açık fırsatlarımız") } }, "boss");
        var self = await service.UpdateAsync((await store.GetAsync(first.EntryId!))! with { Terms = new[] { new GlossaryTerm("açık fırsatlar") } }, "boss");

        Assert.Equal(GlossarySaveStatus.Saved, first.Status);
        Assert.Equal(GlossarySaveStatus.Duplicate, plural.Status);
        Assert.Equal(first.EntryId, plural.EntryId);
        Assert.Equal(GlossarySaveStatus.Duplicate, update.Status);
        Assert.Equal(GlossarySaveStatus.Saved, self.Status);
    }
}
