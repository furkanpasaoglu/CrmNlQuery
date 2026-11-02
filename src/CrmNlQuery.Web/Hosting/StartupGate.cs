using System.Net;
using System.Security.Cryptography;
using System.Text;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Common.Constants;

namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Answers every request with 503 and a waiting page or short JSON until services are ready.
/// </summary>
public static class StartupGate
{
    /// <summary>
    /// Adds the middleware that answers 503 until startup finishes.
    /// </summary>
    public static IApplicationBuilder UseStartupGate(this IApplicationBuilder app, AppStartup startup, ILogger logger)
    {
        return app.Use(async (context, next) =>
        {
            if (startup.IsReady)
            {
                await next();
                return;
            }
            if (startup.ShouldLogGateHit(DateTimeOffset.UtcNow))
            {
                logger.LogWarning("Requests are being answered with the starting page: the service is {State} (attempt {Attempts}).", startup.State, startup.Attempts);
            }
            await WriteAsync(context, startup.State);
        });
    }

    private static async Task WriteAsync(HttpContext context, StartupState state)
    {
        var language = UiLanguage.FromAcceptLanguage(context.Request.Headers.AcceptLanguage.ToString()) ?? UiLanguage.Default;
        var failed = state == StartupState.Failed;
        var title = MessageCatalog.Render(failed ? Msg.StartingFailedTitle : Msg.StartingTitle, language);
        var text = MessageCatalog.Render(failed ? Msg.StartingFailedText : Msg.StartingText, language);
        var path = context.Request.Path.Value ?? "";

        var response = context.Response;
        response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        response.Headers.RetryAfter = "10";
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";

        var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.Equals("/api", StringComparison.OrdinalIgnoreCase);
        if (isApi)
        {
            await response.WriteAsJsonAsync(new { kind = "failure", message = text, error = text, errorKind = "starting" });
            return;
        }
        if (Path.HasExtension(path) && !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.ContentSecurityPolicy = $"default-src 'none'; style-src 'nonce-{nonce}'; frame-ancestors 'none'";
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }
        await response.WriteAsync(Render(language, title, text, nonce), Encoding.UTF8);
    }

    /// <summary>
    /// Renders the localized waiting page HTML with a nonce for the style.
    /// </summary>
    public static string Render(string language, string title, string text, string nonce)
    {
        var heading = WebUtility.HtmlEncode(title);
        var body = WebUtility.HtmlEncode(text);
        return $"<!doctype html><html lang=\"{language}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<meta http-equiv=\"refresh\" content=\"10\">"
            + $"<title>{heading}</title>"
            + $"<style nonce=\"{nonce}\">body{{font-family:Segoe UI,Arial,sans-serif;background:#f4f6fb;color:#111b38;margin:0;display:flex;min-height:100vh;align-items:center;justify-content:center}}"
            + "main{max-width:30rem;margin:1rem;padding:1.5rem 2rem;background:#fff;border-radius:10px;border:1px solid #d9dfee}h1{font-size:1.2rem;margin:0 0 .75rem}p{margin:0;line-height:1.5}"
            + "@media(prefers-color-scheme:dark){body{background:#0d1530;color:#e7ecfa}main{background:#111b38;border-color:#26325a}}</style></head>"
            + $"<body><main><h1>{heading}</h1><p>{body}</p></main></body></html>";
    }
}
