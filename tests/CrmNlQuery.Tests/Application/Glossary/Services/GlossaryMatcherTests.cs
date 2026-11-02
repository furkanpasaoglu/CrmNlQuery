using CrmNlQuery.Application.Glossary.Services;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;

namespace CrmNlQuery.Tests.Application.Glossary.Services;

/// <summary>
/// Tests the glossary matcher: Turkish suffixes, accents, case, multi-word terms, false-positive traps, order and limits.
/// </summary>
public class GlossaryMatcherTests
{
    private static GlossaryEntry Entry(string id, params string[] terms) => new GlossaryEntry
    {
        Id = id, Kind = GlossaryKind.Alias, Entity = "incident", Terms = terms.Select(t => new GlossaryTerm(t)).ToList(),
    };

    private static List<string> Ids(string question, params GlossaryEntry[] entries) => GlossaryMatcher.Match(question, entries).Select(h => h.Entry.Id).ToList();

    /// <summary>
    /// Verifies that inflected and differently cased forms match.
    /// </summary>
    [Theory]
    [InlineData("KOBİ'lerin listesini getir", "KOBİ")]
    [InlineData("KOBI'ler", "KOBİ")]
    [InlineData("sadık müşterilerimizin listesi", "sadık müşteri")]
    [InlineData("Açık fırsatlarımız neler", "açık fırsat")]
    [InlineData("TALEPLERİ getir", "talep")]
    [InlineData("taleplerimizden kaç tane var", "talep")]
    [InlineData("bu hafta ki talepte", "talep")]
    [InlineData("show large companies", "large company")]
    [InlineData("list all cases", "case")]
    [InlineData("open branches", "open branch")]
    public void InflectedAndDifferentlyCasedForms_Match(string question, string term)
    {
        Assert.Equal(new[] { "x" }, Ids(question, Entry("x", term)));
    }

    /// <summary>
    /// Verifies that look alikes and broken phrases do not match.
    /// </summary>
    [Theory]
    [InlineData("talepname dosyasını aç", "talep")]
    [InlineData("vipassana kursu", "VIP")]
    [InlineData("casework files", "case")]
    [InlineData("sadık olan müşteri", "sadık müşteri")]
    [InlineData("müşteri sadık", "sadık müşteri")]
    [InlineData("büyükbaş hayvan", "büyük firma")]
    public void LookAlikesAndBrokenPhrases_DoNotMatch(string question, string term)
    {
        Assert.Empty(Ids(question, Entry("x", term)));
    }

    /// <summary>
    /// Verifies that a short term matches exactly or with an apostrophe suffix.
    /// </summary>
    [Fact]
    public void AShortTerm_MatchesExactlyOrWithAnApostropheSuffix()
    {
        Assert.Equal(new[] { "x" }, Ids("VIP müşterileri getir", Entry("x", "VIP")));
        Assert.Equal(new[] { "x" }, Ids("VIP'lerin sayısı", Entry("x", "VIP")));
        Assert.Empty(Ids("vipler", Entry("x", "VIP")));
    }

    /// <summary>
    /// Verifies that the longer term wins and an overlapping shorter one is skipped.
    /// </summary>
    [Fact]
    public void TheLongerTermWins_AndAnOverlappingShorterOneIsSkipped()
    {
        var big = Entry("big", "büyük firma");
        var firm = Entry("firm", "firma");

        Assert.Equal(new[] { "big" }, Ids("büyük firmaları getir", firm, big));
        Assert.Equal(new[] { "firm" }, Ids("firmaları getir", firm, big));
    }

    /// <summary>
    /// Verifies that several different terms in one question all match, ordered by length.
    /// </summary>
    [Fact]
    public void SeveralDifferentTermsInOneQuestion_AllMatch_InTheOrderOfTheirLength()
    {
        var a = Entry("a", "talep");
        var b = Entry("b", "sadık müşteri");

        var ids = Ids("sadık müşterilerin taleplerini göster", a, b);

        Assert.Equal(new[] { "b", "a" }, ids);
    }

    /// <summary>
    /// Verifies that any term of an entry matches and disabled entries are ignored.
    /// </summary>
    [Fact]
    public void AnyOfTheTermsOfAnEntry_Matches_AndDisabledEntriesAreIgnored()
    {
        var multi = Entry("m", "cari", "hesap kart");
        var off = Entry("off", "talep") with { Enabled = false };

        Assert.Equal(new[] { "m" }, Ids("hesap kartlarını göster", multi, off));
        Assert.Equal(new[] { "m" }, Ids("carileri göster", multi, off));
        Assert.Empty(Ids("talepleri göster", off));
    }

    /// <summary>
    /// Verifies that at most eight entries are returned.
    /// </summary>
    [Fact]
    public void AtMostEightEntries_AreReturned()
    {
        var entries = Enumerable.Range(0, 12).Select(i => Entry("e" + i, "terim" + (char)('a' + i))).ToArray();
        var question = string.Join(" ", entries.Select(e => e.Terms[0].Text));

        Assert.Equal(GlossaryMatcher.MaxHits, Ids(question, entries).Count);
    }

    /// <summary>
    /// Verifies that the matched phrase is reported.
    /// </summary>
    [Fact]
    public void TheMatchedPhrase_IsReported()
    {
        var hit = Assert.Single(GlossaryMatcher.Match("KOBİ'lerin sayısı", new[] { Entry("x", "KOBİ") }));

        Assert.Equal("KOBİ", hit.Phrase);
    }

    /// <summary>
    /// Verifies that an empty question or no entries matches nothing.
    /// </summary>
    [Fact]
    public void AnEmptyQuestionOrNoEntries_MatchesNothing()
    {
        Assert.Empty(Ids("", Entry("x", "talep")));
        Assert.Empty(GlossaryMatcher.Match("talep", new List<GlossaryEntry>()));
    }
}
