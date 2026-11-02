using CrmNlQuery.Web.Security;

namespace CrmNlQuery.Tests.Web.Security;

/// <summary>
/// Tests the new admin panel page, which uses a per-request nonce added only to the style rule.
/// </summary>
public class AdminNextPageTests
{
    /// <summary>
    /// Verifies that the nonce is random per call and safe in a header and an attribute.
    /// </summary>
    [Fact]
    public void TheNonce_IsRandomPerCall_AndSafeInAHeaderAndAnAttribute()
    {
        var a = AdminNextPage.NewNonce();
        var b = AdminNextPage.NewNonce();

        Assert.NotEqual(a, b);
        Assert.Equal(24, a.Length);
        Assert.Matches("^[A-Za-z0-9+/=]+$", a);
    }

    /// <summary>
    /// Verifies that rendering replaces the placeholder everywhere.
    /// </summary>
    [Fact]
    public void Render_ReplacesThePlaceholder_Everywhere()
    {
        var html = "<meta name=\"csp-nonce\" content=\"__CSP_NONCE__\"><p>__CSP_NONCE__</p>";

        var result = AdminNextPage.Render(html, "abc123==");

        Assert.DoesNotContain("__CSP_NONCE__", result);
        Assert.Equal(2, result.Split("abc123==").Length - 1);
    }

    /// <summary>
    /// Verifies that the removed legacy panel addresses redirect to the new panel.
    /// </summary>
    [Theory]
    [InlineData("/admin.html", "/admin-next/")]
    [InlineData("/ADMIN.HTML", "/admin-next/")]
    [InlineData("/admin", "/admin-next/")]
    [InlineData("/admin-next/", null)]
    [InlineData("/api/admin/me", null)]
    [InlineData("/result.html", null)]
    public void TheRemovedLegacyPanelAddresses_GoToTheNewPanel(string path, string? expected)
    {
        Assert.Equal(expected, AdminNextPage.LegacyTarget(path));
    }

    /// <summary>
    /// Verifies that the CSP with a nonce changes only the style rule.
    /// </summary>
    [Fact]
    public void TheCspWithANonce_OnlyChangesTheStyleRule()
    {
        var plain = SecurityHeaders.ContentSecurityPolicy;
        var withNonce = SecurityHeaders.ContentSecurityPolicyWithStyleNonce("XYZ==");

        Assert.Contains("style-src 'self' 'nonce-XYZ==';", withNonce);
        Assert.Contains("script-src 'self';", withNonce);
        Assert.DoesNotContain("unsafe-inline", withNonce);
        Assert.DoesNotContain("unsafe-eval", withNonce);
        Assert.Contains("frame-ancestors 'none'", withNonce);
        Assert.Equal(plain.Replace("style-src 'self';", "style-src 'self' 'nonce-XYZ==';"), withNonce);
    }
}
