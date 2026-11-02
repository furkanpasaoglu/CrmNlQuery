namespace CrmNlQuery.Web.Security;

/// <summary>
/// Adds browser security headers such as nosniff, referrer policy and the content security policy to responses.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    public const string StyleNonceItem = "csp-style-nonce";

    /// <summary>
    /// Builds the content security policy that allows styles with the given nonce.
    /// </summary>
    public static string ContentSecurityPolicyWithStyleNonce(string nonce)
        => ContentSecurityPolicy.Replace("style-src 'self';", $"style-src 'self' 'nonce-{nonce}';");

    /// <summary>
    /// Checks whether a content type is HTML.
    /// </summary>
    public static bool IsHtml(string? contentType)
        => contentType is not null && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds the security headers middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                if (IsHtml(context.Response.ContentType))
                {
                    headers.ContentSecurityPolicy = context.Items[StyleNonceItem] is string nonce ? ContentSecurityPolicyWithStyleNonce(nonce) : ContentSecurityPolicy;
                }
                return Task.CompletedTask;
            });
            await next();
        });
    }
}
