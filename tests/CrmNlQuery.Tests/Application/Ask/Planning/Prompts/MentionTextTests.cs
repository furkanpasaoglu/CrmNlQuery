using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Prompts;

/// <summary>
/// Tests that Turkish suffixes on company names are stripped before record lookup.
/// </summary>
public class MentionTextTests
{
    /// <summary>
    /// Verifies that turkish case and possessive suffixes after an apostrophe are removed.
    /// </summary>
    [Theory]
    [InlineData("Örnekova'nın", "Örnekova")]
    [InlineData("Örnekova'da", "Örnekova")]
    [InlineData("Örnekova'dan", "Örnekova")]
    [InlineData("Örnekova'ya", "Örnekova")]
    [InlineData("ZKV'nin", "ZKV")]
    [InlineData("Brightwave'in", "Brightwave")]
    [InlineData("Denema'nın", "Denema")]
    [InlineData("Denema'ya", "Denema")]
    [InlineData("Zorlu'nun", "Zorlu")]
    [InlineData("Türk Telekom'un", "Türk Telekom")]
    [InlineData("Örnekova’nın", "Örnekova")]
    [InlineData("ORNEKOVA'NIN", "ORNEKOVA")]
    [InlineData("Örnekova Holding'in", "Örnekova Holding")]
    [InlineData("Acme'yle", "Acme")]
    [InlineData("Örnekova'daki", "Örnekova")]
    [InlineData("Hamburg'daki", "Hamburg")]
    public void TurkishCaseAndPossessiveSuffixes_AfterAnApostrophe_AreRemoved(string input, string expected)
    {
        Assert.Equal(expected, MentionText.StripTurkishSuffixes(input));
    }

    /// <summary>
    /// Verifies that real names and unmarked suffixes are left alone.
    /// </summary>
    [Theory]
    [InlineData("Levi's")]
    [InlineData("McDonald's")]
    [InlineData("O'Brien")]
    [InlineData("Rock'n'Roll")]
    [InlineData("Örnekova")]
    [InlineData("Örnekovanın")]
    [InlineData("Denema Sigorta A.Ş.")]
    [InlineData("D'Angelo & Sons")]
    public void RealNamesAndUnmarkedSuffixes_AreLeftAlone(string input)
    {
        Assert.Equal(input, MentionText.StripTurkishSuffixes(input));
    }

    /// <summary>
    /// Verifies that when nothing would be left the original is kept.
    /// </summary>
    [Fact]
    public void WhenNothingWouldBeLeft_TheOriginalIsKept()
    {
        Assert.Equal("'nın", MentionText.StripTurkishSuffixes("'nın"));
    }

    private static readonly IReadOnlyList<EntitySummary> Entities = new List<EntitySummary>
    {
        new EntitySummary("account", "accounts", "Account"),
        new EntitySummary("contract", "contracts", "Contract"),
    };

    /// <summary>
    /// Verifies that the analyzer never hands a suffixed name to the record search.
    /// </summary>
    [Fact]
    public async Task TheAnalyzer_NeverHandsASuffixedNameToTheRecordSearch()
    {
        var reply = """{"primaryEntity":"contract","relatedEntities":["account"],"mentions":[{"text":"Örnekova'nın","entity":"account"},{"text":"Levi's","entity":"account"}],"columns":[],"aggregate":false}""";

        var analysis = await new LlmQuestionAnalyzer(new FakeLlm(reply)).AnalyzeAsync("Örnekova'nın contractlarını getir", Entities);

        Assert.Equal(new[] { "Örnekova", "Levi's" }, analysis.Mentions.Select(m => m.Text));
    }
}
