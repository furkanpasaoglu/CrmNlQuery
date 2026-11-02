using CrmNlQuery.Web.Security;

namespace CrmNlQuery.Tests.Web.Security;

/// <summary>
/// Tests the cross-site guard that restricts which routes browsers may call from other origins.
/// </summary>
public class CrossSiteGuardTests
{
    /// <summary>
    /// Verifies that admin routes accept only same-origin browser requests.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/admin/report", "cross-site", false)]
    [InlineData("GET", "/api/admin/report", "same-site", false)]
    [InlineData("GET", "/api/admin/health", "same-origin", true)]
    [InlineData("GET", "/api/admin/report", "none", true)]
    [InlineData("GET", "/api/admin/report", null, true)]
    public void AdminRoutes_AllowOnlySameOrigin(string method, string path, string? site, bool allowed)
    {
        Assert.Equal(allowed, CrossSiteGuard.IsAllowed(method, path, site));
    }

    /// <summary>
    /// Verifies that write routes, except the widget ask endpoint, accept only same-origin requests.
    /// </summary>
    [Theory]
    [InlineData("POST", "/api/history/abc/star", "cross-site", false)]
    [InlineData("POST", "/api/history/abc/star", "same-site", false)]
    [InlineData("DELETE", "/api/history/abc", "cross-site", false)]
    [InlineData("POST", "/api/result/abc/refine", "same-site", false)]
    [InlineData("POST", "/api/admin/metadata/refresh", "cross-site", false)]
    [InlineData("POST", "/api/history/abc/star", "same-origin", true)]
    [InlineData("DELETE", "/api/history/abc", "same-origin", true)]
    public void WriteRoutes_AllowOnlySameOrigin(string method, string path, string? site, bool allowed)
    {
        Assert.Equal(allowed, CrossSiteGuard.IsAllowed(method, path, site));
    }

    /// <summary>
    /// Verifies that widget routes and static files stay open to cross-origin calls from the CRM page.
    /// </summary>
    [Theory]
    [InlineData("POST", "/api/ask", "same-site", true)]
    [InlineData("POST", "/api/ask", "cross-site", true)]
    [InlineData("GET", "/api/history", "same-site", true)]
    [InlineData("GET", "/api/result/abc", "same-site", true)]
    [InlineData("OPTIONS", "/api/ask", "cross-site", true)]
    [InlineData("GET", "/widget/widget.js", "cross-site", true)]
    [InlineData("GET", "/index.html", "cross-site", true)]
    public void WidgetRoutesAndStaticFiles_StayOpen(string method, string path, string? site, bool allowed)
    {
        Assert.Equal(allowed, CrossSiteGuard.IsAllowed(method, path, site));
    }

    /// <summary>
    /// Verifies that admin routes are matched case-insensitively and include the path base.
    /// </summary>
    [Fact]
    public void AdminRoutes_AreMatchedCaseInsensitively_AndIncludingThePathBase()
    {
        Assert.False(CrossSiteGuard.IsAllowed("GET", "/API/Admin/Report", "cross-site"));
        Assert.False(CrossSiteGuard.IsAllowed("GET", "/api/admin", "cross-site"));
    }

    private static readonly string[] Crm = { "http://crm.example" };

    /// <summary>
    /// Verifies that chat write routes are open only to the allowed CRM origin.
    /// </summary>
    [Theory]
    [InlineData("PATCH", "/api/chats/abc", "same-site", "http://crm.example", true)]
    [InlineData("DELETE", "/api/chats/abc", "cross-site", "http://crm.example", true)]
    [InlineData("DELETE", "/api/chats", "same-site", "http://CRM.example", true)]
    [InlineData("PATCH", "/api/chats/abc", "cross-site", "http://evil.example", false)]
    [InlineData("DELETE", "/api/chats", "cross-site", "http://evil.example", false)]
    [InlineData("DELETE", "/api/chats/abc", "same-site", null, false)]
    [InlineData("DELETE", "/api/chats/abc", "same-site", "", false)]
    [InlineData("PATCH", "/api/chats/abc", "same-origin", null, true)]
    [InlineData("GET", "/api/chats", "cross-site", "http://evil.example", true)]
    [InlineData("GET", "/api/chats/abc/messages", "same-site", null, true)]
    [InlineData("POST", "/api/chatsfoo", "cross-site", "http://crm.example", false)]
    [InlineData("POST", "/api/result/abc123/open-in-crm", "same-site", "http://crm.example", true)]
    [InlineData("POST", "/api/result/abc123/open-in-crm", "cross-site", "http://evil.example", false)]
    [InlineData("POST", "/api/result/abc123/open-in-crm", "same-site", null, false)]
    [InlineData("POST", "/api/result/abc123/explain/edit", "same-site", "http://crm.example", true)]
    [InlineData("POST", "/api/result/abc123/explain/edit", "cross-site", "http://evil.example", false)]
    [InlineData("POST", "/api/result/abc123/explain/edit", "same-site", null, false)]
    [InlineData("POST", "/api/result/abc123/refine", "same-site", "http://crm.example", false)]
    [InlineData("PUT", "/api/me/preferences", "same-site", "http://crm.example", true)]
    [InlineData("PUT", "/api/me/preferences", "cross-site", "http://evil.example", false)]
    [InlineData("PUT", "/api/me/preferences", "same-site", null, false)]
    [InlineData("GET", "/api/me/preferences", "cross-site", "http://evil.example", true)]
    [InlineData("PUT", "/api/me/preferencesx", "same-site", "http://crm.example", false)]
    public void ChatWriteRoutes_AreOpenOnlyToTheAllowedCrmOrigin(string method, string path, string site, string? origin, bool allowed)
    {
        Assert.Equal(allowed, CrossSiteGuard.IsAllowed(method, path, site, origin, Crm));
    }

    /// <summary>
    /// Verifies that without an allowed origin list chat writes from other origins are refused.
    /// </summary>
    [Fact]
    public void WithoutAnAllowedOriginList_ChatWritesFromOtherOriginsAreRefused()
    {
        Assert.False(CrossSiteGuard.IsAllowed("DELETE", "/api/chats/abc", "same-site"));
    }
}
