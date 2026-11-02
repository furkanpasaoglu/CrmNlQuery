using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Application.Reports.Services;

namespace CrmNlQuery.Tests.Application.Reports.Services;

/// <summary>
/// Holds tests for report search intent and matching in Turkish and English.
/// </summary>
public class ReportMatcherTests
{
    private static ReportItem R(string name, string? description = null) => new ReportItem(Guid.NewGuid().ToString("D"), name, description, "http://crm/viewer");

    private static readonly IReadOnlyList<ReportItem> Reports = new List<ReportItem>
    {
        R("Neglected Cases", "Shows cases that nobody worked on for a number of days"),
        R("Account Summary", "Overview of an account"),
        R("Top Knowledge Base Articles"),
        R("Hesap Özeti", "Müşteri hesabının özeti"),
        R("Satış Hunisi"),
    };

    /// <summary>
    /// Verifies that report requests are recognised.
    /// </summary>
    [Theory]
    [InlineData("Neglected Cases raporunu aç")]
    [InlineData("hesap özeti raporu")]
    [InlineData("Raporları göster")]
    [InlineData("open the Account Summary report")]
    [InlineData("Show me the reports")]
    [InlineData("satış hunisi report")]
    [InlineData("RAPOR")]
    public void ReportRequestsAreRecognised(string question)
    {
        Assert.True(ReportMatcher.Matches(question));
    }

    /// <summary>
    /// Verifies that data questions and other text are not treated as report requests.
    /// </summary>
    [Theory]
    [InlineData("kaç rapor var")]
    [InlineData("How many reports do we have")]
    [InlineData("rapor sayısı nedir")]
    [InlineData("raporlanan vakaları getir")]
    [InlineData("Show accounts in London")]
    [InlineData("Brightwave'in fırsatlarını getir")]
    [InlineData("en çok raporu olan müşteriyi göster")]
    [InlineData("")]
    public void DataQuestionsAndOtherTextAreNotTreatedAsReportRequests(string question)
    {
        Assert.False(ReportMatcher.Matches(question));
    }

    /// <summary>
    /// Verifies that aLong sentence without an opening verb is not aReport request.
    /// </summary>
    [Fact]
    public void ALongSentenceWithoutAnOpeningVerbIsNotAReportRequest()
    {
        Assert.False(ReportMatcher.Matches("Geçen çeyrekte müşterilerin bize gönderdiği rapor ekleri hakkında ne düşünüyorsun acaba"));
    }

    /// <summary>
    /// Verifies that the name is found ignoring the words around it.
    /// </summary>
    [Fact]
    public void TheNameIsFoundIgnoringTheWordsAroundIt()
    {
        var (items, listedAll) = ReportMatcher.Find("Neglected Cases raporunu aç", Reports);

        Assert.False(listedAll);
        Assert.Equal("Neglected Cases", Assert.Single(items).Name);
    }

    /// <summary>
    /// Verifies that turkish suffixes and letters are tolerated.
    /// </summary>
    [Fact]
    public void TurkishSuffixesAndLettersAreTolerated()
    {
        var (items, _) = ReportMatcher.Find("hesap özetini aç raporunu", Reports);

        Assert.Equal("Hesap Özeti", items[0].Name);
        var (upper, _) = ReportMatcher.Find("SATIŞ HUNİSİ RAPORU", Reports);
        Assert.Equal("Satış Hunisi", Assert.Single(upper).Name);
    }

    /// <summary>
    /// Verifies that the description is searched too but the name ranks first.
    /// </summary>
    [Fact]
    public void TheDescriptionIsSearchedToo_ButTheNameRanksFirst()
    {
        var (items, _) = ReportMatcher.Find("account summary report", Reports);

        Assert.Equal("Account Summary", items[0].Name);
        var (byDescription, _) = ReportMatcher.Find("cases nobody worked on report", Reports);
        Assert.Equal("Neglected Cases", byDescription[0].Name);
    }

    /// <summary>
    /// Verifies that without meaningful words the first reports are listed by name.
    /// </summary>
    [Fact]
    public void WithoutMeaningfulWordsTheFirstReportsAreListedByName()
    {
        var many = Enumerable.Range(0, 14).Select(i => R("Report " + (char)('A' + i))).Reverse().ToList();

        var (items, listedAll) = ReportMatcher.Find("raporları göster", many);

        Assert.True(listedAll);
        Assert.Equal(10, items.Count);
        Assert.Equal("Report A", items[0].Name);
    }

    /// <summary>
    /// Verifies that aName that matches nothing gives no items so the question continues normally.
    /// </summary>
    [Fact]
    public void ANameThatMatchesNothingGivesNoItems_SoTheQuestionContinuesNormally()
    {
        var (items, _) = ReportMatcher.Find("Zzxyqq raporunu aç", Reports);

        Assert.Empty(items);
    }

    /// <summary>
    /// Verifies that at most five reports are returned and short fragments do not match.
    /// </summary>
    [Fact]
    public void AtMostFiveReportsAreReturnedAndShortFragmentsDoNotMatch()
    {
        var many = Enumerable.Range(0, 9).Select(i => R("Sales Pipeline " + i)).ToList();

        Assert.Equal(5, ReportMatcher.Find("sales pipeline report", many).Items.Count);
        Assert.Empty(ReportMatcher.Find("acc raporu", Reports).Items);
    }
}
