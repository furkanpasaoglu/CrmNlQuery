namespace CrmNlQuery.Web.Security;

/// <summary>
/// Runtime safety net that fails closed with a 500 and a log entry when an unmarked /api endpoint matches.
/// </summary>
public static class EndpointGuard
{
    /// <summary>
    /// Checks whether an endpoint carries one of the required markers.
    /// </summary>
    public static bool IsMarked(Endpoint endpoint)
    {
        return endpoint.Metadata.GetMetadata<RequireCallerMarker>() is not null
            || endpoint.Metadata.GetMetadata<RequireAdminMarker>() is not null
            || endpoint.Metadata.GetMetadata<PublicMarker>() is not null;
    }

    /// <summary>
    /// Checks whether the current request matched an /api endpoint that has no marker.
    /// </summary>
    public static bool IsUnmarkedApiEndpoint(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint is not null && context.Request.Path.StartsWithSegments("/api") && !IsMarked(endpoint);
    }

    /// <summary>
    /// Adds the middleware that rejects requests to unmarked /api endpoints.
    /// </summary>
    public static IApplicationBuilder UseEndpointMarkerGuard(this IApplicationBuilder app, ILogger logger)
    {
        return app.Use(async (context, next) =>
        {
            if (IsUnmarkedApiEndpoint(context))
            {
                logger.LogError("Unmarked API endpoint {Endpoint} was blocked; mark it with RequireCaller, RequireAdmin or Public.", context.GetEndpoint()!.DisplayName);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }
            await next();
        });
    }
}
