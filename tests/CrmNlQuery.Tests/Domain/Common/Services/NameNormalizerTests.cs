using System.Globalization;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Tests.Domain.Common.Services;

/// <summary>
/// Tests name normalization used for case- and culture-independent matching.
/// </summary>
public class NameNormalizerTests
{
    /// <summary>
    /// Verifies that normalize folds turkish letters and strips legal suffix.
    /// </summary>
    [Theory]
    [InlineData("ISPARTA", "isparta")]
    [InlineData("ısparta", "isparta")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("Örnekova Holding A.Ş.", "ornekova holding")]
    [InlineData("Denema Sigorta A.Ş.", "denema sigorta")]
    [InlineData("Sakura Home Tech KK", "sakura home tech")]
    [InlineData("ZKV", "zkv")]
    [InlineData("Co", "co")]
    [InlineData("  Harbor   Logistik GmbH ", "harbor logistik")]
    public void Normalize_FoldsTurkishLettersAndStripsLegalSuffix(string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.Normalize(input));
    }

    /// <summary>
    /// Verifies that normalize is independent of current culture.
    /// </summary>
    [Fact]
    public void Normalize_IsIndependentOfCurrentCulture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("isparta", NameNormalizer.Normalize("ISPARTA"));
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("isparta", NameNormalizer.Normalize("ISPARTA"));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    /// <summary>
    /// Verifies that to like pattern turns turkish letters into character classes.
    /// </summary>
    [Fact]
    public void ToLikePattern_TurnsTurkishLettersIntoCharacterClasses()
    {
        Assert.Equal("%[cCçÇ][iIıİ]k[iIıİ][sSşŞ]%", NameNormalizer.ToLikePattern("Çıkış"));
    }

    /// <summary>
    /// Verifies that to like pattern escapes SQL like wildcards.
    /// </summary>
    [Fact]
    public void ToLikePattern_EscapesSqlLikeWildcards()
    {
        Assert.Equal("%50[%][_][[]x%", NameNormalizer.ToLikePattern("50%_[x"));
    }

    /// <summary>
    /// Verifies that to like pattern keeps ampersand and quote untouched.
    /// </summary>
    [Fact]
    public void ToLikePattern_KeepsAmpersandAndQuoteUntouched()
    {
        Assert.Contains("&", NameNormalizer.ToLikePattern("Tire & Auto"));
        Assert.Contains("'", NameNormalizer.ToLikePattern("O'Brien"));
    }
}
