using CrmNlQuery.Domain.Ask.Steps;

namespace CrmNlQuery.Tests.Domain.Ask.Steps;

/// <summary>
/// Tests the detection of conditional question wording.
/// </summary>
public class ConditionalQuestionFilterTests
{
    /// <summary>
    /// Verifies that conditional wording is detected.
    /// </summary>
    [Theory]
    [InlineData("Denema contractları yoksa Örnekova contractlarını getir")]
    [InlineData("DENEMA CONTRACTLARI YOKSA ORNEKOVA")]
    [InlineData("Denema'nın contractı yok ise Örnekova'nınkini göster")]
    [InlineData("Denema contractı bulunmazsa Örnekova")]
    [InlineData("Denema'da contract olmazsa Örnekova'ya bak")]
    [InlineData("Denema contractları boşsa Örnekova")]
    [InlineData("Denema contractları değilse Örnekova")]
    [InlineData("Denema sonuç çıkmazsa Örnekova")]
    [InlineData("Denema kayıt dönmezse Örnekova")]
    [InlineData("Contracts of Denema, if none then Ornekova")]
    [InlineData("Contracts of Denema. If there are no contracts, show Ornekova")]
    [InlineData("Contracts of Denema, if empty show Ornekova")]
    [InlineData("Show Denema contracts, otherwise Ornekova contracts")]
    [InlineData("Denema contracts if not Ornekova")]
    [InlineData("Denema contracts, else Ornekova")]
    public void ConditionalWording_IsDetected(string question)
    {
        Assert.True(ConditionalQuestionFilter.MayBeConditional(question));
    }

    /// <summary>
    /// Verifies that plain questions are not flagged.
    /// </summary>
    [Theory]
    [InlineData("Bütün productları getir")]
    [InlineData("Denema Sigorta A.Ş. ve ZKV Örnek Sistemleri A.Ş. şirketlerine ait contractları getir")]
    [InlineData("How many accounts do we have?")]
    [InlineData("Hamburg'daki şirketleri (accounts) getir")]
    [InlineData("")]
    public void PlainQuestions_AreNotFlagged(string question)
    {
        Assert.False(ConditionalQuestionFilter.MayBeConditional(question));
    }

    /// <summary>
    /// Verifies that words that merely contain a marker are not flagged.
    /// </summary>
    [Fact]
    public void WordsThatOnlyContainAMarker_AreNotFlagged()
    {
        Assert.False(ConditionalQuestionFilter.MayBeConditional("Show accounts located elsewhere"));
        Assert.False(ConditionalQuestionFilter.MayBeConditional("Yoksulluk Vakfı contractlarını getir"));
    }
}
