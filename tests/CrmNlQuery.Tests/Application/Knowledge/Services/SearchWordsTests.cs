using CrmNlQuery.Application.Knowledge.Services;

namespace CrmNlQuery.Tests.Application.Knowledge.Services;

/// <summary>
/// Tests extraction of search words from questions for knowledge search.
/// </summary>
public class SearchWordsTests
{
    /// <summary>
    /// Verifies that command and question words are dropped so they do not pull in unrelated notes.
    /// </summary>
    [Fact]
    public void CommandAndQuestionWords_AreDropped_SoTheyDoNotPullInUnrelatedNotes()
    {
        Assert.Equal(new[] { "Productları" }, SearchWords.Extract("Productları getir"));
        Assert.Equal(new[] { "KOBİ", "müşterileri" }, SearchWords.Extract("KOBİ müşterileri getir"));
        Assert.Equal(new[] { "big", "opportunities" }, SearchWords.Extract("Show me the big opportunities"));
    }

    /// <summary>
    /// Verifies that distinctive terms and code names stay.
    /// </summary>
    [Fact]
    public void DistinctiveTerms_AndCodeNames_Stay()
    {
        Assert.Equal(new[] { "new_musteriSegmenti", "alanı" }, SearchWords.Extract("new_musteriSegmenti alanı nedir"));
    }

    /// <summary>
    /// Verifies that short words are dropped and duplicates ignoring case and turkish letters are merged.
    /// </summary>
    [Fact]
    public void ShortWords_AreDropped_AndDuplicatesIgnoringCaseAndTurkishLetters_AreMerged()
    {
        Assert.Equal(new[] { "KOBİ" }, SearchWords.Extract("ve bir KOBİ kobi KOBI"));
    }

    /// <summary>
    /// Verifies that there is an upper bound on how many words are used.
    /// </summary>
    [Fact]
    public void ThereIsAnUpperBound_OnHowManyWordsAreUsed()
    {
        var text = string.Join(" ", Enumerable.Range(0, 100).Select(i => "kelime" + i));

        Assert.Equal(SearchWords.MaxWords, SearchWords.Extract(text).Count);
    }

    /// <summary>
    /// Verifies that null or blank input gives no words.
    /// </summary>
    [Fact]
    public void NullOrBlank_GivesNoWords()
    {
        Assert.Empty(SearchWords.Extract(null));
        Assert.Empty(SearchWords.Extract("   "));
    }
}
