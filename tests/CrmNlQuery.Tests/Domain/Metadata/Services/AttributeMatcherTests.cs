using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Domain.Metadata.Services;

namespace CrmNlQuery.Tests.Domain.Metadata.Services;

/// <summary>
/// Tests matching of attribute-like tokens to real attribute names.
/// </summary>
public class AttributeMatcherTests
{
    private static readonly EntityInfo Product = TestMetadata.Product();

    /// <summary>
    /// Verifies that tokens such as id map to the expected attribute names.
    /// </summary>
    [Theory]
    [InlineData("id", "productid")]
    [InlineData("ID", "productid")]
    [InlineData("name", "name")]
    [InlineData("productid", "productid")]
    [InlineData("Product Number", "productnumber")]
    public void Match_ResolvesExactAndSpecialTokens(string token, string expected)
    {
        Assert.Equal(expected, AttributeMatcher.Match(token, Product).Attribute?.LogicalName);
    }

    /// <summary>
    /// Verifies that an unknown token suggests names that contain it.
    /// </summary>
    [Fact]
    public void Match_UnknownToken_SuggestsContainedNames()
    {
        var result = AttributeMatcher.Match("productname", Product);
        Assert.Null(result.Attribute);
        Assert.Contains(result.Suggestions, s => s.LogicalName == "name");
    }

    /// <summary>
    /// Verifies that gibberish has no suggestions.
    /// </summary>
    [Fact]
    public void Match_Gibberish_HasNoSuggestions()
    {
        var result = AttributeMatcher.Match("zzzzqq", Product);
        Assert.Null(result.Attribute);
        Assert.Empty(result.Suggestions);
    }
}
