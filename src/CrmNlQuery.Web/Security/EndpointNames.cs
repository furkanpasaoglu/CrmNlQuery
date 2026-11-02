namespace CrmNlQuery.Web.Security;

/// <summary>
/// Builds a METHOD /route-template name for an endpoint, without values or query string so it is safe for audit records.
/// </summary>
public static class EndpointNames
{
    /// <summary>
    /// Builds the endpoint name for the current request without values or query string.
    /// </summary>
    public static string For(HttpContext http)
    {
        var pattern = (http.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value ?? "";
        return http.Request.Method + " " + pattern;
    }
}
