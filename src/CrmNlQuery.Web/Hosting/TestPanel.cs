namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Defines the plain test panel page used for development, which is not part of the product.
/// </summary>
public static class TestPanel
{
    /// <summary>
    /// Returns whether the test panel is enabled by setting or development environment.
    /// </summary>
    public static bool IsEnabled(string? setting, bool isDevelopment)
    {
        return bool.TryParse(setting, out var configured) ? configured : isDevelopment;
    }

    /// <summary>
    /// Checks whether a path points at the test panel page.
    /// </summary>
    public static bool IsTestPanelPath(string path)
    {
        return path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds middleware that returns 404 for the root and index page when the test panel is disabled.
    /// </summary>
    public static IApplicationBuilder UseTestPanelGate(this IApplicationBuilder app, bool enabled)
    {
        return app.Use(async (context, next) =>
        {
            if (!enabled && IsTestPanelPath(context.Request.Path.Value ?? ""))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await next();
        });
    }
}
