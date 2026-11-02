using System.Net;
using System.Net.Http.Json;
using CrmNlQuery.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrmNlQuery.Tests.Web.Security;

/// <summary>
/// HTTP tests that every /api endpoint is deliberately marked (RequireCaller, RequireAdmin or Public), rejects unauthenticated calls before the handler runs, and fails for unmarked endpoints.
/// </summary>
[Collection("WebHost")]
public class EndpointMarkersTests : IClassFixture<WebSecurityTests.Host>
{
    private readonly WebSecurityTests.Host _host;

    public EndpointMarkersTests(WebSecurityTests.Host host)
    {
        _host = host;
    }

    private IReadOnlyList<RouteEndpoint> ApiEndpoints()
    {
        _host.CreateClient();
        return _host.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? "").StartsWith("/api")).ToList();
    }

    private static string Name(RouteEndpoint e) => string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods) + " " + e.RoutePattern.RawText;

    /// <summary>
    /// Verifies that there are API endpoints and every one is marked.
    /// </summary>
    [Fact]
    public void ThereAreApiEndpoints_AndEveryOneIsMarked()
    {
        var endpoints = ApiEndpoints();

        Assert.True(endpoints.Count > 30, "Expected endpoint count not found; the inventory scan may be broken.");
        var unmarked = endpoints.Where(e => !EndpointGuard.IsMarked(e)).Select(Name).ToList();
        Assert.True(unmarked.Count == 0, "Unmarked endpoints: " + string.Join("; ", unmarked));
    }

    /// <summary>
    /// Verifies that no endpoint is public today.
    /// </summary>
    [Fact]
    public void NoEndpointIsPublic_Today()
    {
        Assert.DoesNotContain(ApiEndpoints(), e => e.Metadata.GetMetadata<PublicMarker>() is not null);
    }

    /// <summary>
    /// Verifies that every marked endpoint refuses a request without identity before the handler runs.
    /// </summary>
    [Fact]
    public async Task EveryMarkedEndpoint_RefusesARequestWithoutIdentity_BeforeTheHandlerRuns()
    {
        var client = _host.CreateClient();
        var wrong = new List<string>();

        foreach (var endpoint in ApiEndpoints())
        {
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.First();
            var path = (endpoint.RoutePattern.RawText ?? "").Replace("{id:guid}", Guid.NewGuid().ToString()).Replace("{id}", "x");
            var request = new HttpRequestMessage(new HttpMethod(method), path + "?account=a&group=g&key=k");
            if (method is "POST" or "PUT" or "PATCH")
            {
                request.Content = JsonContent.Create(new { });
            }
            var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                wrong.Add($"{Name(endpoint)} -> {(int)response.StatusCode}");
            }
        }

        Assert.True(wrong.Count == 0, "Requests without identity did not get 401: " + string.Join("; ", wrong));
    }

    private static HttpContext ContextFor(string path, params object[] metadata)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        return context;
    }

    /// <summary>
    /// Verifies that the safety net flags an API endpoint without a marker.
    /// </summary>
    [Fact]
    public void TheSafetyNet_FlagsAnApiEndpointWithoutAMarker()
    {
        Assert.True(EndpointGuard.IsUnmarkedApiEndpoint(ContextFor("/api/new-thing")));
    }

    /// <summary>
    /// Verifies that the safety net accepts each marker.
    /// </summary>
    [Fact]
    public void TheSafetyNet_AcceptsEachMarker()
    {
        Assert.False(EndpointGuard.IsUnmarkedApiEndpoint(ContextFor("/api/x", new RequireCallerMarker())));
        Assert.False(EndpointGuard.IsUnmarkedApiEndpoint(ContextFor("/api/x", new RequireAdminMarker("Users.Manage"))));
        Assert.False(EndpointGuard.IsUnmarkedApiEndpoint(ContextFor("/api/x", new PublicMarker("health probe"))));
    }

    /// <summary>
    /// Verifies that the safety net ignores paths outside API and requests without an endpoint.
    /// </summary>
    [Fact]
    public void TheSafetyNet_IgnoresPathsOutsideApi_AndRequestsWithoutAnEndpoint()
    {
        Assert.False(EndpointGuard.IsUnmarkedApiEndpoint(ContextFor("/widget/widget.js")));
        var noEndpoint = new DefaultHttpContext();
        noEndpoint.Request.Path = "/api/unknown";
        Assert.False(EndpointGuard.IsUnmarkedApiEndpoint(noEndpoint));
    }
}
