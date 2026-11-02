using CrmNlQuery.Composition;
using CrmNlQuery.Web.Identity;
using CrmNlQuery.Web.Security;
using Serilog;
using Serilog.Extensions.Logging;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddAppConfiguration(builder.Configuration["History:ConnectionString"], SetupComposition.KeyFilePath(builder.Configuration));
builder.Configuration.AddCommandLine(args);

if (args.Contains("--reset-setup"))
{
    await SetupComposition.ResetAsync(builder.Configuration["History:ConnectionString"]);
    Console.WriteLine("The setup settings were deleted. Start the service again to run the wizard.");
    return;
}

if (SetupComposition.Needed(builder.Configuration))
{
    await SetupHost.RunAsync(builder);
    return;
}

var logSink = WebLogging.Configure(builder);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = RequestLimits.DefaultBytes);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = RequestLimits.DefaultBytes);
builder.WebHost.UseUrls(builder.Configuration["Service:Url"] ?? "http://localhost:5080");

var loggerFactory = new SerilogLoggerFactory(Log.Logger);
var startup = new AppStartup();
var lateEndpoints = new LateEndpointDataSource();

var allowedOrigins = CorsOrigins.Resolve(builder.Configuration["Cors:AllowedOrigins"], AppComposition.CrmBaseUrl(builder.Configuration));
builder.Services.AddWidgetCors(allowedOrigins);
var testPanelEnabled = TestPanel.IsEnabled(builder.Configuration["UI:TestPanel"], builder.Environment.IsDevelopment());

builder.Services.AddSingleton(startup);
builder.Services.AddSingleton(provider => provider.GetRequiredService<AppStartup>().Services);
builder.Services.AddSingleton(provider => new CallerIdentifier(provider.GetRequiredService<AppServices>().Identity));
builder.Services.AddSingleton(provider =>
{
    var services = provider.GetRequiredService<AppServices>();
    return new AdminAuthorizer(services.Identity, services.IdentityOptions, new AdminAccess(services.Access), services.Access, loggerFactory.CreateLogger("CrmNlQuery.Admin"));
});

var syncSeconds = int.TryParse(builder.Configuration["Startup:SyncSeconds"], out var configuredSync) && configuredSync >= 0 ? configuredSync : 20;
builder.Services.AddHostedService(provider => new StartupHostedService(
    startup,
    token => AppComposition.CreateAsync(builder.Configuration, loggerFactory, builder.Environment.EnvironmentName, token),
    services =>
    {
        logSink.Attach(services.LogStore);
        var routes = new CollectingRouteBuilder(provider);
        EndpointMap.MapAll(routes, services, testPanelEnabled, allowedOrigins);
        services.StartRetention(loggerFactory.CreateLogger("CrmNlQuery.Retention"), provider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
        lateEndpoints.Set(routes.Collect());
    },
    loggerFactory.CreateLogger("CrmNlQuery.Startup"),
    provider.GetRequiredService<IHostApplicationLifetime>(),
    TimeSpan.FromSeconds(syncSeconds)));

var app = builder.Build();
((IEndpointRouteBuilder)app).DataSources.Add(lateEndpoints);

app.UseStartupGate(startup, loggerFactory.CreateLogger("CrmNlQuery.Startup"));
app.UseSecurityHeaders();
app.UseTestPanelGate(testPanelEnabled);
app.UseAdminNextPage(app.Environment);
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers["Cache-Control"] = "no-cache",
});
app.UsePrivateNetworkAccess(allowedOrigins);
app.UseCors();
app.UseCrossSiteGuard(allowedOrigins);
app.UseBearerAuthentication();
app.UseRequestBodyLimits();
app.UseNoStoreForApi();
app.UseRequestLanguage();
app.UseEndpointMarkerGuard(loggerFactory.CreateLogger("CrmNlQuery.Endpoints"));

app.Lifetime.ApplicationStopped.Register(logSink.Dispose);

app.Run();

/// <summary>
/// Exposes the application entry point so integration tests using WebApplicationFactory can reach it.
/// </summary>
public partial class Program { }
