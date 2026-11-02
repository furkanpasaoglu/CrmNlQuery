using System.Security.Cryptography;
using System.Text;

namespace CrmNlQuery.Web.Security;

/// <summary>
/// Serves the new React admin panel page with a per-request CSP nonce instead of as a static file.
/// </summary>
public static class AdminNextPage
{
    public const string Folder = "admin-next";
    public const string Placeholder = "__CSP_NONCE__";

    /// <summary>
    /// Generates a random 24-character base64 nonce usable as a CSP nonce.
    /// </summary>
    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    /// <summary>
    /// Maps removed legacy admin panel addresses to the new panel, or returns null for other paths.
    /// </summary>
    public static string? LegacyTarget(string path) =>
        string.Equals(path, "/admin.html", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "/admin", StringComparison.OrdinalIgnoreCase)
            ? "/" + Folder + "/"
            : null;

    /// <summary>
    /// Renders the panel's index template with the given nonce inserted.
    /// </summary>
    public static string Render(string template, string nonce) => template.Replace(Placeholder, nonce, StringComparison.Ordinal);

    /// <summary>
    /// Adds the middleware that serves the new admin panel page.
    /// </summary>
    public static IApplicationBuilder UseAdminNextPage(this IApplicationBuilder app, IWebHostEnvironment environment)
    {
        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "";
            var isGet = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            if (!isGet)
            {
                await next();
                return;
            }

            var legacy = LegacyTarget(path);
            if (legacy is not null)
            {
                context.Response.Redirect(legacy, permanent: false);
                return;
            }

            if (string.Equals(path, "/" + Folder, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/" + Folder + "/", permanent: false);
                return;
            }

            if (string.Equals(path, "/" + Folder + "/", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "/" + Folder + "/index.html", StringComparison.OrdinalIgnoreCase))
            {
                var file = environment.WebRootFileProvider.GetFileInfo(Folder + "/index.html");
                if (!file.Exists)
                {
                    await next();
                    return;
                }
                string template;
                await using (var stream = file.CreateReadStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    template = await reader.ReadToEndAsync();
                }
                var nonce = NewNonce();
                context.Items[SecurityHeaders.StyleNonceItem] = nonce;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync(Render(template, nonce));
                return;
            }

            await next();
        });
    }
}
