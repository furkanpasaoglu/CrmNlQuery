using CrmNlQuery.Web.Security;

namespace CrmNlQuery.Tests.Web.Security;

/// <summary>
/// Tests the security headers added to responses.
/// </summary>
public class SecurityHeadersTests
{
    /// <summary>
    /// Verifies that the CSP applies only to HTML content types.
    /// </summary>
    [Theory]
    [InlineData("text/html", true)]
    [InlineData("text/html; charset=utf-8", true)]
    [InlineData("TEXT/HTML", true)]
    [InlineData("application/json", false)]
    [InlineData("text/javascript", false)]
    [InlineData("text/css", false)]
    [InlineData(null, false)]
    public void OnlyHtmlResponses_GetTheCsp(string? contentType, bool expected)
    {
        Assert.Equal(expected, SecurityHeaders.IsHtml(contentType));
    }

    /// <summary>
    /// Verifies that the CSP never allows inline or eval code and forbids framing.
    /// </summary>
    [Fact]
    public void TheCsp_NeverAllowsInlineOrEvalCode_AndForbidsFraming()
    {
        var csp = SecurityHeaders.ContentSecurityPolicy;

        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("*", csp);
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'none'", csp);
    }
}
