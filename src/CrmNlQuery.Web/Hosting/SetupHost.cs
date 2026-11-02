using CrmNlQuery.Composition;
using CrmNlQuery.Web.Endpoints.Setup;
using CrmNlQuery.Web.Security;

namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Runs the small web server used for first-run setup: the wizard page and its endpoints only, with the one-time setup code written to the console.
/// </summary>
public static class SetupHost
{
    /// <summary>
    /// Starts the setup server and returns when it stops, which happens after setup completes.
    /// </summary>
    public static async Task RunAsync(WebApplicationBuilder builder)
    {
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = RequestLimits.DefaultBytes);
        builder.WebHost.UseUrls(builder.Configuration["Service:Url"] ?? "http://localhost:5080");

        var code = Application.Setup.Services.SetupService.NewCode();
        var setup = SetupComposition.CreateService(builder.Configuration, code);
        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CrmNlQuery.Setup");

        app.UseSecurityHeaders();
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "/";
            if (path == "/" || path == "/setup")
            {
                context.Response.Redirect("/setup/");
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            await next();
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        SetupEndpoints.Map(app, setup, app.Lifetime);

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            logger.LogWarning("FIRST-RUN SETUP. Open /setup in a browser and enter this one-time code: {Code}", code);
        });
        await app.RunAsync();
    }
}
