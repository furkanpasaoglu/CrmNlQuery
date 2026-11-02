using Microsoft.AspNetCore.Http.Features;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;

namespace CrmNlQuery.Web.Security;

/// <summary>
/// Defines request body size limits for the service.
/// </summary>
public static class RequestLimits
{
    public const long DocumentBytes = 256 * 1024;
    public const long ApiBytes = 64 * 1024;
    public const long DefaultBytes = 1024 * 1024;

    /// <summary>
    /// Returns the maximum body size in bytes for a request path.
    /// </summary>
    public static long MaxBytesFor(string path)
    {
        if (path.StartsWith("/api/admin/documents", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentBytes;
        }
        return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ? ApiBytes : DefaultBytes;
    }

    /// <summary>
    /// Adds the request body size limits middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseRequestBodyLimits(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var max = MaxBytesFor(context.Request.Path.Value ?? "");
            if (context.Request.ContentLength is { } length && length > max)
            {
                await TooLarge(context, max);
                return;
            }
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = max;
            }
            try
            {
                await next();
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
            {
                await TooLarge(context, max);
            }
        });
    }

    private static Task TooLarge(HttpContext context, long max)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return context.Response.WriteAsJsonAsync(new { error = MessageCatalog.Now(Msg.RequestTooLarge, MessageCatalog.Args(("limit", (max / 1024).ToString()))), kind = "request_too_large" });
    }
}
