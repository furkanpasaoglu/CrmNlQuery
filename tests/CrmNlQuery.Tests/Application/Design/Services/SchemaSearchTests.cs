using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Application.Design.Services;

namespace CrmNlQuery.Tests.Application.Design.Services;

/// <summary>
/// Tests schema search matching for the design assistant.
/// </summary>
public class SchemaSearchTests
{
    private static DesignConcept Concept(string term, params string[] synonyms) => new DesignConcept { Term = term, Synonyms = synonyms.ToList() };

    /// <summary>
    /// Verifies that Turkish terms match across suffixes and diacritics.
    /// </summary>
    [Theory]
    [InlineData("araç", "arac", true)]
    [InlineData("aracı", "arac", true)]
    [InlineData("Servis Kaydı", "servis kaydi", true)]
    [InlineData("randevu", "servis kaydi", false)]
    [InlineData("kaydı", "servis kaydi", true)]
    [InlineData("ab", "abc", false)]
    [InlineData("vehicle", "arac", false)]
    public void TermMatches_FoldsTurkishLettersAndToleratesSuffixes(string term, string text, bool expected)
    {
        Assert.Equal(expected, SchemaSearch.TermMatches(term, text));
    }

    /// <summary>
    /// Verifies that custom table names are recognized by prefix while standard ones are not.
    /// </summary>
    [Theory]
    [InlineData("new_arac", true)]
    [InlineData("account", false)]
    [InlineData("msdyn_workorder", false)]
    [InlineData("adx_webpage", false)]
    public void IsCustom_MeansAPublisherPrefixThatIsNotMicrosofts(string name, bool expected)
    {
        Assert.Equal(expected, SchemaSearch.IsCustom(name));
    }

    /// <summary>
    /// Verifies that DetectPrefix returns the most common custom prefix, or new.
    /// </summary>
    [Fact]
    public void DetectPrefix_IsTheMostCommonCustomPrefix_OrNew()
    {
        Assert.Equal("acme", SchemaSearch.DetectPrefix(new[] { "account", "acme_a", "acme_b", "new_x" }));
        Assert.Equal("new", SchemaSearch.DetectPrefix(new[] { "account", "contact" }));
    }

    /// <summary>
    /// Verifies that a service appointment request finds the existing service record and vehicle tables and not unrelated ones.
    /// </summary>
    [Fact]
    public void ServiceAppointmentRequest_FindsTheExistingServiceRecordAndVehicleTables_NotUnrelatedOnes()
    {
        var concepts = new[] { Concept("servis randevusu", "servis kaydı", "randevu", "appointment"), Concept("araç", "vehicle") };

        var hits = SchemaSearch.ScoreEntities(DesignTestData.Summaries, concepts);
        var matches = SchemaSearch.Match(DesignTestData.All, hits, concepts);

        Assert.Equal("new_servis_kaydi", matches[0].Entity);
        Assert.Contains(matches, m => m.Entity == "new_arac" && m.IsCustom);
        Assert.DoesNotContain(matches, m => m.Entity == "account");
        Assert.Contains(matches[0].Fields, f => f.Logical == "new_aracid");
        Assert.Contains("new_tutar", matches[0].CustomFields);
    }

    /// <summary>
    /// Verifies that an unrelated request has no candidates.
    /// </summary>
    [Fact]
    public void AnUnrelatedRequest_HasNoCandidates()
    {
        var concepts = new[] { Concept("eğitim katılımı", "training", "attendance") };

        var hits = SchemaSearch.ScoreEntities(DesignTestData.Summaries, concepts);

        Assert.Empty(hits);
        Assert.Empty(SchemaSearch.Match(DesignTestData.All, hits, concepts));
    }

    /// <summary>
    /// Verifies that a multi word term matches on its meaningful words through suffixes.
    /// </summary>
    [Fact]
    public void AMultiWordTerm_MatchesOnItsMeaningfulWords_ThroughSuffixes()
    {
        Assert.True(SchemaSearch.TermMatches("servis randevusu", "servis kaydi"));
    }

    /// <summary>
    /// Verifies that generic words such as status or date are not treated as meaningful search terms.
    /// </summary>
    [Theory]
    [InlineData("status")]
    [InlineData("durum")]
    [InlineData("tarih")]
    [InlineData("date")]
    public void GenericWords_NeverMatchOnTheirOwn(string term)
    {
        Assert.False(SchemaSearch.TermMatches(term, "booking status date durum tarih"));
    }

    /// <summary>
    /// Verifies that a suffixed term does not match an unrelated longer word.
    /// </summary>
    [Fact]
    public void ASuffixedTermDoesNotMatchAnUnrelatedLongerWord()
    {
        Assert.False(SchemaSearch.TermMatches("status", "subscription statistic outlook"));
    }

    /// <summary>
    /// Verifies that a standard table needs a name match and field similarity alone does not make it a candidate.
    /// </summary>
    [Fact]
    public void AStandardTable_NeedsANameMatch_FieldSimilarityAloneDoesNotMakeItACandidate()
    {
        var generic = new CrmNlQuery.Domain.Metadata.Entities.EntityInfo("actioncarduserstate", "x", "ActionCardUserState", "id", "name",
            new List<CrmNlQuery.Domain.Metadata.Entities.AttributeInfo> { new CrmNlQuery.Domain.Metadata.Entities.AttributeInfo("startdate", "Start Date", "DateTime", new List<CrmNlQuery.Domain.Metadata.Entities.OptionInfo>()),
                new CrmNlQuery.Domain.Metadata.Entities.AttributeInfo("randevutarihi", "Randevu Tarihi", "DateTime", new List<CrmNlQuery.Domain.Metadata.Entities.OptionInfo>()) },
            new List<CrmNlQuery.Domain.Metadata.Entities.RelationshipInfo>());
        var concepts = new[] { Concept("randevu") };

        var matches = SchemaSearch.Match(new[] { generic }, SchemaSearch.ScoreEntities(new[] { new CrmNlQuery.Domain.Metadata.Entities.EntitySummary("actioncarduserstate", "x", "ActionCardUserState") }, concepts), concepts);

        Assert.Empty(matches);
    }

    /// <summary>
    /// Verifies that proposed standard tables become candidates only when they really exist.
    /// </summary>
    [Fact]
    public void ProposedStandardTables_BecomeCandidates_OnlyWhenTheyReallyExist()
    {
        var concepts = new[] { Concept("şikayet") };

        var hits = SchemaSearch.ScoreEntities(DesignTestData.Summaries, concepts, new[] { "incident", "no_such_table", "INCIDENT " });

        Assert.Equal(new[] { "incident" }, hits.Select(h => h.Entity));
    }

    /// <summary>
    /// Verifies that custom tables get a tie break bonus.
    /// </summary>
    [Fact]
    public void CustomTablesGetATieBreakBonus()
    {
        var concepts = new[] { Concept("araç"), Concept("case") };

        var hits = SchemaSearch.ScoreEntities(DesignTestData.Summaries, concepts);
        var matches = SchemaSearch.Match(DesignTestData.All, hits, concepts);

        Assert.True(matches.First(m => m.Entity == "new_arac").Score >= matches.First(m => m.Entity == "incident").Score);
    }

    /// <summary>
    /// Verifies that a label match outranks a logical name match.
    /// </summary>
    [Fact]
    public void ALabelMatch_OutranksALogicalNameMatch()
    {
        var concepts = new[] { Concept("case") };

        var hits = SchemaSearch.ScoreEntities(DesignTestData.Summaries, concepts);

        Assert.Equal("incident", hits[0].Entity);
    }
}
