using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CrmNlQuery.Tests.Web.Security;

/// <summary>
/// Holds HTTP tests of CORS, cross-site protection, security headers and body limits against the real pipeline.
/// </summary>
[Collection("WebHost")]
public class WebSecurityTests : IClassFixture<WebSecurityTests.Host>
{
    private const string CrmOrigin = "http://crm.example";

    private static readonly string[] IsolatedKeys =
    {
        "Identity__Mode", "Identity__FixedUser", "Identity__AllowFixedInProduction",
        "Llm__Provider", "Llm__AllowCloud", "Anthropic__ApiKey", "History__ConnectionString",
    };

    private static void Isolate()
    {
        Environment.SetEnvironmentVariable("Identity__Mode", "Unconfigured");
        Environment.SetEnvironmentVariable("Identity__FixedUser", "");
        Environment.SetEnvironmentVariable("Identity__AllowFixedInProduction", "false");
        Environment.SetEnvironmentVariable("Llm__Provider", "Ollama");
        Environment.SetEnvironmentVariable("Llm__AllowCloud", "false");
        Environment.SetEnvironmentVariable("Anthropic__ApiKey", "");
        Environment.SetEnvironmentVariable("History__ConnectionString", "");
    }

    private static void Restore()
    {
        foreach (var key in IsolatedKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// Test web host factory for the security tests.
    /// </summary>
    public sealed class Host : WebApplicationFactory<Program>
    {
        public Host()
        {
            Isolate();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Crm:BaseUrl", "http://crm.example/org");
            builder.UseSetting("Setup:Completed", "true");
            builder.UseSetting("Cors:AllowedOrigins", CrmOrigin);
            builder.UseSetting("Logging:File:Path", Path.Combine(Path.GetTempPath(), "nlquery-tests", "log-.txt"));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Restore();
        }
    }

    private readonly HttpClient _client;

    public WebSecurityTests(Host host)
    {
        _client = host.CreateClient();
    }

    private static HttpRequestMessage Preflight(string path, string origin, string method = "POST")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }

    private static string? AllowedOrigin(HttpResponseMessage response)
        => response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;

    /// <summary>
    /// Verifies that widget endpoint allows the preflight from the CRM origin with credentials.
    /// </summary>
    [Fact]
    public async Task WidgetEndpoint_AllowsThePreflight_FromTheCrmOrigin_WithCredentials()
    {
        var response = await _client.SendAsync(Preflight("/api/ask", CrmOrigin));

        Assert.Equal(CrmOrigin, AllowedOrigin(response));
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    /// <summary>
    /// Verifies that the other widget read endpoints are also open to the CRM origin.
    /// </summary>
    [Theory]
    [InlineData("/api/result/0123456789abcdef0123456789abcdef", "GET")]
    public async Task TheOtherWidgetReadEndpoints_AreAlsoOpenToTheCrmOrigin(string path, string method)
    {
        var response = await _client.SendAsync(Preflight(path, CrmOrigin, method));
        Assert.Equal(CrmOrigin, AllowedOrigin(response));
    }

    /// <summary>
    /// Verifies that the chat endpoints allow the preflight for the CRM origin and announce their method.
    /// </summary>
    [Theory]
    [InlineData("/api/chats", "GET")]
    [InlineData("/api/chats/counts", "GET")]
    [InlineData("/api/chats/0123456789abcdef0123456789abcdef/messages", "GET")]
    [InlineData("/api/chats/0123456789abcdef0123456789abcdef", "PATCH")]
    [InlineData("/api/chats/0123456789abcdef0123456789abcdef", "DELETE")]
    [InlineData("/api/chats", "DELETE")]
    [InlineData("/api/me/preferences", "GET")]
    [InlineData("/api/me/preferences", "PUT")]
    [InlineData("/api/result/0123456789abcdef0123456789abcdef/open-in-crm", "POST")]
    [InlineData("/api/result/0123456789abcdef0123456789abcdef/explain/edit", "POST")]
    [InlineData("/api/result/0123456789abcdef0123456789abcdef/count", "GET")]
    public async Task TheChatEndpoints_AllowThePreflight_ForTheCrmOrigin_AndAnnounceTheirMethod(string path, string method)
    {
        var response = await _client.SendAsync(Preflight(path, CrmOrigin, method));

        Assert.Equal(CrmOrigin, AllowedOrigin(response));
        var methods = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods"));
        Assert.Contains(method, methods);
    }

    /// <summary>
    /// Verifies that the chat endpoints never allow an unknown origin.
    /// </summary>
    [Theory]
    [InlineData("/api/chats", "DELETE")]
    [InlineData("/api/chats/0123456789abcdef0123456789abcdef", "PATCH")]
    [InlineData("/api/me/preferences", "PUT")]
    public async Task TheChatEndpoints_NeverAllowAnUnknownOrigin(string path, string method)
    {
        var response = await _client.SendAsync(Preflight(path, "http://evil.example", method));
        Assert.Null(AllowedOrigin(response));
    }

    private static HttpRequestMessage ChatRequest(HttpMethod method, string path, string site, string? origin)
    {
        var request = WithSite(method, path, site);
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
        return request;
    }

    /// <summary>
    /// Verifies that chat writes from another site are refused unless they come from the CRM origin.
    /// </summary>
    [Fact]
    public async Task ChatWrites_FromAnotherSite_AreRefused_UnlessTheyComeFromTheCrmOrigin()
    {
        var evil = await _client.SendAsync(ChatRequest(HttpMethod.Delete, "/api/chats", "cross-site", "http://evil.example"));
        var noOrigin = await _client.SendAsync(ChatRequest(HttpMethod.Delete, "/api/chats", "cross-site", null));
        var crm = await _client.SendAsync(ChatRequest(HttpMethod.Delete, "/api/chats", "same-site", CrmOrigin));

        Assert.Equal(HttpStatusCode.Forbidden, evil.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, crm.StatusCode);
    }

    /// <summary>
    /// Verifies that the chat endpoints never answer without an identity.
    /// </summary>
    [Fact]
    public async Task TheChatEndpoints_NeverAnswerWithoutAnIdentity()
    {
        foreach (var path in new[] { "/api/chats", "/api/chats/counts", "/api/chats/0123456789abcdef0123456789abcdef/messages" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(path)).StatusCode);
        }
        var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/chats/0123456789abcdef0123456789abcdef")
        {
            Content = new StringContent("{\"title\":\"x\"}", System.Text.Encoding.UTF8, "application/json"),
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(patch)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.DeleteAsync("/api/chats/0123456789abcdef0123456789abcdef")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.DeleteAsync("/api/chats")).StatusCode);
    }

    /// <summary>
    /// Verifies that the record count never answers without an identity.
    /// </summary>
    [Fact]
    public async Task TheRecordCount_NeverAnswersWithoutAnIdentity()
    {
        const string path = "/api/result/0123456789abcdef0123456789abcdef/count";

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(WithSite(HttpMethod.Get, path, "same-site"))).StatusCode);
    }

    /// <summary>
    /// Verifies that the result page shows the count and the hints without inline code or HTML injection.
    /// </summary>
    [Fact]
    public async Task TheResultPage_ShowsTheCountAndTheHints_WithoutInlineCodeOrHtmlInjection()
    {
        var script = await _client.GetStringAsync("/result.js");

        Assert.Contains("/count", script);
        Assert.Contains("narrowBox", script);
        Assert.DoesNotContain("innerHTML", script);
    }

    /// <summary>
    /// Verifies that open in CRM never answers without an identity and cross site calls are refused.
    /// </summary>
    [Fact]
    public async Task OpenInCrm_NeverAnswersWithoutAnIdentity_AndCrossSiteCallsAreRefused()
    {
        const string path = "/api/result/0123456789abcdef0123456789abcdef/open-in-crm";
        var plain = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(plain)).StatusCode);

        var evil = ChatRequest(HttpMethod.Post, path, "cross-site", "http://evil.example");
        var crm = ChatRequest(HttpMethod.Post, path, "same-site", CrmOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(evil)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(crm)).StatusCode);
    }

    /// <summary>
    /// Verifies that the preferences never answer without an identity and cross site writes are refused.
    /// </summary>
    [Fact]
    public async Task ThePreferences_NeverAnswerWithoutAnIdentity_AndCrossSiteWritesAreRefused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/me/preferences")).StatusCode);
        var put = new HttpRequestMessage(HttpMethod.Put, "/api/me/preferences")
        {
            Content = new StringContent("{\"displayName\":\"x\"}", System.Text.Encoding.UTF8, "application/json"),
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(put)).StatusCode);

        var evil = ChatRequest(HttpMethod.Put, "/api/me/preferences", "cross-site", "http://evil.example");
        var crm = ChatRequest(HttpMethod.Put, "/api/me/preferences", "same-site", CrmOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(evil)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(crm)).StatusCode);
    }

    /// <summary>
    /// Verifies that an unknown origin is never allowed even on aWidget endpoint.
    /// </summary>
    [Fact]
    public async Task AnUnknownOrigin_IsNeverAllowed_EvenOnAWidgetEndpoint()
    {
        var response = await _client.SendAsync(Preflight("/api/ask", "http://evil.example"));
        Assert.Null(AllowedOrigin(response));
    }

    /// <summary>
    /// Verifies that admin and write endpoints have no CORS at all even for the CRM origin.
    /// </summary>
    [Theory]
    [InlineData("/api/admin/report", "GET")]
    [InlineData("/api/admin/health", "GET")]
    [InlineData("/api/result/0123456789abcdef0123456789abcdef/refine", "POST")]
    [InlineData("/api/admin/metadata/refresh", "POST")]
    public async Task AdminAndWriteEndpoints_HaveNoCorsAtAll_EvenForTheCrmOrigin(string path, string method)
    {
        var response = await _client.SendAsync(Preflight(path, CrmOrigin, method));
        Assert.Null(AllowedOrigin(response));
    }

    private static HttpRequestMessage WithSite(HttpMethod method, string path, string site)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Sec-Fetch-Site", site);
        if (method != HttpMethod.Get)
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }
        return request;
    }

    /// <summary>
    /// Verifies that admin endpoints refuse requests from other origins.
    /// </summary>
    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    public async Task AdminEndpoints_RefuseRequestsFromOtherOrigins(string site)
    {
        var response = await _client.SendAsync(WithSite(HttpMethod.Get, "/api/admin/report", site));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Verifies that admin endpoints from the same origin reach the identity gate which refuses without an identity.
    /// </summary>
    [Fact]
    public async Task AdminEndpoints_FromTheSameOrigin_ReachTheIdentityGate_WhichRefusesWithoutAnIdentity()
    {
        var response = await _client.SendAsync(WithSite(HttpMethod.Get, "/api/admin/report", "same-origin"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Verifies that write endpoints refuse requests from other origins.
    /// </summary>
    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    public async Task WriteEndpoints_RefuseRequestsFromOtherOrigins(string site)
    {
        var refine = await _client.SendAsync(WithSite(HttpMethod.Post, "/api/result/abc/refine", site));
        var metadata = await _client.SendAsync(WithSite(HttpMethod.Post, "/api/admin/metadata/refresh", site));
        Assert.Equal(HttpStatusCode.Forbidden, refine.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, metadata.StatusCode);
    }

    /// <summary>
    /// Verifies that widget endpoints accept requests from the CRM page and then ask for an identity.
    /// </summary>
    [Fact]
    public async Task WidgetEndpoints_AcceptRequestsFromTheCrmPage_AndThenAskForAnIdentity()
    {
        var ask = await _client.SendAsync(WithSite(HttpMethod.Post, "/api/ask", "same-site"));
        var chats = await _client.SendAsync(WithSite(HttpMethod.Get, "/api/chats", "same-site"));
        Assert.Equal(HttpStatusCode.Unauthorized, ask.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, chats.StatusCode);
    }

    /// <summary>
    /// Verifies that anonymous requests never get data without an identity.
    /// </summary>
    [Fact]
    public async Task AnonymousRequests_NeverGetData_WithoutAnIdentity()
    {
        foreach (var path in new[] { "/api/chats", "/api/result/0123456789abcdef0123456789abcdef", "/api/admin/report", "/api/admin/health" })
        {
            var response = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>
    /// Verifies that static files must be revalidated so an upgrade reaches users at once.
    /// </summary>
    [Fact]
    public async Task StaticFiles_MustBeRevalidated_SoAnUpgradeReachesUsersAtOnce()
    {
        foreach (var path in new[] { "/widget/widget.js", "/result.js", "/result.html" })
        {
            var response = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var control = response.Headers.CacheControl;
            Assert.True(control is { NoCache: true }, path + " must send Cache-Control: no-cache (revalidate with the ETag)");
            Assert.NotNull(response.Headers.ETag);
        }
    }

    /// <summary>
    /// Verifies that the test panel is served in development.
    /// </summary>
    [Fact]
    public async Task TheTestPanel_IsServedInDevelopment()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/index.html")).StatusCode);
    }

    /// <summary>
    /// Verifies that the test panel is off in production but the product pages stay available and the setting turns it on.
    /// </summary>
    [Fact]
    public async Task TheTestPanel_IsOffInProduction_ButTheProductPagesStayAvailable_AndTheSettingTurnsItOn()
    {
        Isolate();
        try
        {
            using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Crm:BaseUrl", "http://crm.example/org");
                builder.UseSetting("Setup:Completed", "true");
                builder.UseSetting("Logging:File:Path", Path.Combine(Path.GetTempPath(), "nlquery-tests", "log-.txt"));
            });
            var client = production.CreateClient();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/index.html")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/result.html")).StatusCode);
            var noRedirect = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var legacy = await noRedirect.GetAsync("/admin.html");
            Assert.Equal(HttpStatusCode.Redirect, legacy.StatusCode);
            Assert.Equal("/admin-next/", legacy.Headers.Location?.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/widget/widget.js")).StatusCode);

            using var forced = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Crm:BaseUrl", "http://crm.example/org");
                builder.UseSetting("Setup:Completed", "true");
                builder.UseSetting("UI:TestPanel", "true");
                builder.UseSetting("Logging:File:Path", Path.Combine(Path.GetTempPath(), "nlquery-tests", "log-.txt"));
            });
            Assert.Equal(HttpStatusCode.OK, (await forced.CreateClient().GetAsync("/index.html")).StatusCode);
        }
        finally
        {
            Restore();
        }
    }

    /// <summary>
    /// Verifies that fixed identity in production prevents the service from starting.
    /// </summary>
    [Fact]
    public void FixedIdentity_InProduction_PreventsTheServiceFromStarting()
    {
        Isolate();
        Environment.SetEnvironmentVariable("Identity__Mode", "Fixed");
        Environment.SetEnvironmentVariable("Identity__FixedUser", @"CONTOSO\someone");
        try
        {
            using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Crm:BaseUrl", "http://crm.example/org");
                builder.UseSetting("Setup:Completed", "true");
                builder.UseSetting("Logging:File:Path", Path.Combine(Path.GetTempPath(), "nlquery-tests", "log-.txt"));
            });

            var ex = Assert.ThrowsAny<Exception>(() => production.CreateClient());
            Assert.Contains("AllowFixedInProduction", ex.ToString());
        }
        finally
        {
            Restore();
        }
    }

    private static async Task<HttpResponseMessage> PostBytes(HttpClient client, string path, int bytes)
    {
        var content = new ByteArrayContent(new byte[bytes]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return await client.PostAsync(path, content);
    }

    /// <summary>
    /// Verifies that aBody over the api limit is refused with413 and aJson message.
    /// </summary>
    [Fact]
    public async Task ABodyOverTheApiLimit_IsRefusedWith413_AndAJsonMessage()
    {
        var response = await PostBytes(_client, "/api/ask", 70 * 1024);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("request_too_large", body);
        Assert.Contains("64 KB", body);
    }

    /// <summary>
    /// Verifies that aBody under the limit is not refused for size.
    /// </summary>
    [Fact]
    public async Task ABodyUnderTheLimit_IsNotRefusedForSize()
    {
        var response = await PostBytes(_client, "/api/ask", 1024);

        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    /// <summary>
    /// Verifies that the document endpoints allow aLarger body than the other apis.
    /// </summary>
    [Fact]
    public async Task TheDocumentEndpoints_AllowALargerBodyThanTheOtherApis()
    {
        var response = await PostBytes(_client, "/api/admin/documents", 100 * 1024);

        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PostBytes(_client, "/api/admin/documents", 300 * 1024)).StatusCode);
    }

    /// <summary>
    /// Verifies that every HTML page carries the CSP with no inline script or style and no framing.
    /// </summary>
    [Theory]
    [InlineData("/result.html")]
    [InlineData("/index.html")]
    public async Task EveryHtmlPage_CarriesTheCspWithNoInlineScriptOrStyleAndNoFraming(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("style-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    /// <summary>
    /// Verifies that no HTML page contains inline scripts styles or event handlers.
    /// </summary>
    [Theory]
    [InlineData("/result.html")]
    [InlineData("/index.html")]
    public async Task NoHtmlPage_ContainsInlineScriptsStylesOrEventHandlers(string path)
    {
        var html = await _client.GetStringAsync(path);
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(html, "<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline);

        Assert.DoesNotMatch(@"<script(?![^>]*\ssrc=)[^>]*>", withoutComments);
        Assert.DoesNotContain("<style", withoutComments);
        Assert.DoesNotMatch(@"\sstyle\s*=", withoutComments);
        Assert.DoesNotMatch(@"\son[a-z]+\s*=", withoutComments);
    }

    /// <summary>
    /// Verifies that the externalized scripts and styles are served with nosniff but without aCsp.
    /// </summary>
    [Theory]
    [InlineData("/result.js")]
    [InlineData("/result.css")]
    [InlineData("/index.js")]
    [InlineData("/index.css")]
    public async Task TheExternalizedScriptsAndStyles_AreServed_WithNosniff_ButWithoutACsp(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    /// <summary>
    /// Verifies that the widget script and api answers are not given aCsp but are nosniff.
    /// </summary>
    [Fact]
    public async Task TheWidgetScript_AndApiAnswers_AreNotGivenACsp_ButAreNosniff()
    {
        var widget = await _client.GetAsync("/widget/widget.js");
        var api = await _client.GetAsync("/api/admin/me");

        Assert.False(widget.Headers.Contains("Content-Security-Policy"));
        Assert.False(api.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", widget.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("nosniff", api.Headers.GetValues("X-Content-Type-Options").Single());
    }

    /// <summary>
    /// Verifies that even aRefusal or aNot found carries the base headers.
    /// </summary>
    [Fact]
    public async Task EvenARefusalOrANotFound_CarriesTheBaseHeaders()
    {
        var response = await _client.GetAsync("/api/admin/does-not-exist");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    /// <summary>
    /// Verifies that the widget script uses the chat API without the retired controls and without blocking dialogs.
    /// </summary>
    [Fact]
    public async Task TheWidgetScript_UsesTheChatApi_WithoutTheRetiredControls_AndWithoutBlockingDialogs()
    {
        var script = await _client.GetStringAsync("/widget/widget.js");

        Assert.Contains("chatId: state.currentId", script);
        Assert.Contains("/api/chats", script);
        Assert.Contains("/api/me/preferences", script);
        Assert.Contains("/open-in-crm", script);
        Assert.Contains("d.openInCrm", script);
        Assert.Contains("/count'", script);
        Assert.Contains("d.suggestions", script);
        Assert.Contains("/suggest/apply", script);
        Assert.DoesNotContain("previousResultId", script);
        Assert.DoesNotContain("forceNew", script);
        Assert.DoesNotContain("Ask as a new question", script);
        Assert.DoesNotContain("Start over", script);
        Assert.DoesNotContain("My queries", script);
        Assert.DoesNotContain("/api/history", script);
        Assert.DoesNotContain("innerHTML", script);
        Assert.DoesNotContain("outerHTML", script);
        Assert.DoesNotContain("insertAdjacentHTML", script);
        Assert.DoesNotMatch(@"\b(confirm|alert|prompt)\s*\(", script);
        Assert.DoesNotContain("document.write", script);
        Assert.Contains("var T_EN = {", script);
        Assert.Contains("var T_TR = {", script);
    }

    /// <summary>
    /// Verifies that the retired history page is gone.
    /// </summary>
    [Theory]
    [InlineData("/history.html")]
    [InlineData("/history.js")]
    [InlineData("/history.css")]
    public async Task TheRetiredHistoryPage_IsGone(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(path)).StatusCode);
    }
}
