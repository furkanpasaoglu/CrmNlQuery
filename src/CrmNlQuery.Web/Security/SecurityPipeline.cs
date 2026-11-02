namespace CrmNlQuery.Web.Security;

/// <summary>
/// Provides the HTTP security middleware and CORS policy, whose order matters in Program.cs.
/// </summary>
public static class SecurityPipeline
{
    /// <summary>
    /// Adds a CORS policy allowing only the known CRM origins to call the service from the widget.
    /// </summary>
    public static IServiceCollection AddWidgetCors(this IServiceCollection services, IReadOnlyList<string> allowedOrigins)
    {
        return services.AddCors(options => options.AddPolicy("crm-widget", policy =>
        {
            if (allowedOrigins.Count > 0)
            {
                policy.WithOrigins(allowedOrigins.ToArray()).WithHeaders("Content-Type", "Authorization").WithMethods("POST", "GET", "PATCH", "PUT", "DELETE").AllowCredentials();
            }
        }));
    }

    /// <summary>
    /// Adds the private network access header only for allowed origins so Chrome lets CRM pages call a local address.
    /// </summary>
    public static IApplicationBuilder UsePrivateNetworkAccess(this IApplicationBuilder app, IReadOnlyList<string> allowedOrigins)
    {
        return app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString().ToLowerInvariant();
            if (context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network") && allowedOrigins.Contains(origin))
            {
                context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
            }
            await next();
        });
    }

    /// <summary>
    /// Adds the cross-site guard so admin and write endpoints accept only same-origin requests, plus allowed CRM origins for chat writes.
    /// </summary>
    public static IApplicationBuilder UseCrossSiteGuard(this IApplicationBuilder app, IReadOnlyList<string> allowedOrigins)
    {
        return app.Use(async (context, next) =>
        {
            if (!CrossSiteGuard.IsAllowed(context.Request.Method, context.Request.Path.Value ?? "", context.Request.Headers["Sec-Fetch-Site"].ToString(),
                context.Request.Headers.Origin.ToString(), allowedOrigins))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        });
    }

    /// <summary>
    /// Resolves each API request's UI language from preference, Accept-Language or default and exposes it for the request.
    /// </summary>
    public static IApplicationBuilder UseRequestLanguage(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var services = context.RequestServices.GetRequiredService<CrmNlQuery.Composition.AppServices>();
                var account = services.Identity.GetAccount(CrmNlQuery.Web.Identity.WindowsAccount.From(context.User));
                var language = await services.Language.ResolveAsync(account, context.Request.Headers.AcceptLanguage.ToString(), context.RequestAborted);
                CrmNlQuery.Application.Localization.Services.RequestLanguage.Set(language);
            }
            await next();
        });
    }

    /// <summary>
    /// Adds no-store headers so browsers and proxies never cache user-specific API responses.
    /// </summary>
    public static IApplicationBuilder UseNoStoreForApi(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Headers.CacheControl = "no-store";
            }
            await next();
        });
    }
}
