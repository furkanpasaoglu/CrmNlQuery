using CrmNlQuery.Web.Security;

namespace CrmNlQuery.Tests.Web.Security;

/// <summary>
/// Tests parsing of the allowed CORS origins setting.
/// </summary>
public class CorsOriginsTests
{
    private const string CrmUrl = "http://crm.example.com/contoso";

    /// <summary>
    /// Verifies that when not configured the default is the CRM origin without its path.
    /// </summary>
    [Fact]
    public void NotConfigured_DefaultsToTheCrmOrigin_WithoutThePath()
    {
        Assert.Equal(new[] { "http://crm.example.com" }, CorsOrigins.Resolve(null, CrmUrl));
        Assert.Equal(new[] { "http://crm.example.com" }, CorsOrigins.Resolve("   ", CrmUrl));
    }

    /// <summary>
    /// Verifies that a configured list replaces the default rather than adding to it.
    /// </summary>
    [Fact]
    public void Configured_ReplacesTheDefault_NotAddsToIt()
    {
        var result = CorsOrigins.Resolve("https://crm.example.com", CrmUrl);
        Assert.Equal(new[] { "https://crm.example.com" }, result);
    }

    /// <summary>
    /// Verifies that the list is split on comma and semicolon, trimmed and de-duplicated.
    /// </summary>
    [Fact]
    public void ListIsSplitOnCommaAndSemicolon_Trimmed_AndDeduplicated()
    {
        var result = CorsOrigins.Resolve(" http://a.local ; http://b.local:8080 , http://a.local ", CrmUrl);
        Assert.Equal(new[] { "http://a.local", "http://b.local:8080" }, result);
    }

    /// <summary>
    /// Verifies that path and trailing slash are dropped.
    /// </summary>
    [Fact]
    public void PathAndTrailingSlash_AreDropped()
    {
        var result = CorsOrigins.Resolve("http://a.local/contoso/, http://b.local/", CrmUrl);
        Assert.Equal(new[] { "http://a.local", "http://b.local" }, result);
    }

    /// <summary>
    /// Verifies that origins are lowercased.
    /// </summary>
    [Fact]
    public void OriginsAreLowercased()
    {
        Assert.Equal(new[] { "http://crm.local" }, CorsOrigins.Resolve("HTTP://CRM.Local", CrmUrl));
    }

    /// <summary>
    /// Verifies that invalid or wildcard entries are dropped.
    /// </summary>
    [Theory]
    [InlineData("*")]
    [InlineData("not a url")]
    [InlineData("ftp://a.local")]
    [InlineData("javascript:alert(1)")]
    public void InvalidOrWildcardEntries_AreDropped(string entry)
    {
        Assert.Empty(CorsOrigins.Resolve(entry, CrmUrl));
    }

    /// <summary>
    /// Verifies that invalid entries are dropped but valid ones stay.
    /// </summary>
    [Fact]
    public void InvalidEntriesAreDropped_ButValidOnesStay()
    {
        var result = CorsOrigins.Resolve("*, http://a.local", CrmUrl);
        Assert.Equal(new[] { "http://a.local" }, result);
    }

    /// <summary>
    /// Verifies that no config and an invalid CRM URL give an empty list.
    /// </summary>
    [Fact]
    public void NoConfigAndInvalidCrmUrl_GivesEmptyList()
    {
        Assert.Empty(CorsOrigins.Resolve(null, ""));
        Assert.Empty(CorsOrigins.Resolve(null, "not a url"));
    }
}
