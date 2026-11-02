using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CrmNlQuery.Application.Operations.Startup;
using CrmNlQuery.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CrmNlQuery.Web.Hosting;

namespace CrmNlQuery.Tests.Web.Hosting;

/// <summary>
/// Holds tests for the startup gate that serves a waiting page until setup completes.
/// </summary>
public class StartupGateTests
{
    private static AppServices FakeServices() => (AppServices)RuntimeHelpers.GetUninitializedObject(typeof(AppServices));

    /// <summary>
    /// Verifies that while the service starts requests get a 503 waiting page and the endpoints appear when it is ready.
    /// </summary>
    [Fact]
    public async Task While_the_service_starts_requests_get_a_503_waiting_page_and_the_endpoints_appear_when_it_is_ready()
    {
        var startup = new AppStartup();
        var late = new LateEndpointDataSource();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        ((IEndpointRouteBuilder)app).DataSources.Add(late);
        app.UseStartupGate(startup, NullLogger.Instance);
        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();

            var page = await client.GetAsync("/");
            var html = await page.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, page.StatusCode);
            Assert.Contains("The service is starting", html);
            Assert.Contains("http-equiv=\"refresh\"", html);
            Assert.Equal("no-store", page.Headers.CacheControl?.ToString());
            Assert.True(page.Headers.Contains("Retry-After"));
            Assert.DoesNotContain("<script", html);

            var turkish = new HttpRequestMessage(HttpMethod.Get, "/");
            turkish.Headers.AcceptLanguage.ParseAdd("tr-TR,tr;q=0.9");
            var trHtml = WebUtility.HtmlDecode(await (await client.SendAsync(turkish)).Content.ReadAsStringAsync());
            Assert.Contains("Hizmet açılıyor", trHtml);

            var api = await client.GetAsync("/api/anything");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, api.StatusCode);
            using var json = JsonDocument.Parse(await api.Content.ReadAsStringAsync());
            Assert.Equal("failure", json.RootElement.GetProperty("kind").GetString());
            Assert.Equal("starting", json.RootElement.GetProperty("errorKind").GetString());
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));

            var script = await client.GetAsync("/widget/widget.js");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, script.StatusCode);
            Assert.Equal(string.Empty, await script.Content.ReadAsStringAsync());

            var release = new TaskCompletionSource();
            var run = startup.RunAsync(
                async token => { await release.Task; return FakeServices(); },
                services =>
                {
                    var routes = new CollectingRouteBuilder(app.Services);
                    routes.MapGet("/api/hello", () => "hello");
                    late.Set(routes.Collect());
                },
                NullLogger.Instance, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), CancellationToken.None);
            Assert.False(startup.IsReady);
            release.SetResult();
            await run;

            Assert.True(startup.IsReady);
            var hello = await client.GetAsync("/api/hello");
            Assert.Equal(HttpStatusCode.OK, hello.StatusCode);
            Assert.Equal("hello", await hello.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/unknown")).StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>
    /// Verifies that retries while the dependency is unreachable and logs every try.
    /// </summary>
    [Fact]
    public async Task Retries_while_the_dependency_is_unreachable_and_logs_every_try()
    {
        var startup = new AppStartup();
        var attempts = 0;
        var logger = new ListLogger();

        await startup.RunAsync(
            token =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException<AppServices>(new StartupDependencyException("PostgreSQL did not become reachable", canRetry: true, new IOException("refused")))
                    : Task.FromResult(FakeServices());
            },
            _ => { }, logger, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), CancellationToken.None, delay: (_, _) => Task.CompletedTask);

        Assert.True(startup.IsReady);
        Assert.Equal(3, startup.Attempts);
        Assert.Equal(2, logger.Messages.Count(m => m.Contains("keeps trying")));
    }

    /// <summary>
    /// Verifies that a permanent dependency error shows the failed state and recovers when fixed.
    /// </summary>
    [Fact]
    public async Task A_permanent_dependency_error_shows_the_failed_state_and_recovers_when_fixed()
    {
        var startup = new AppStartup();
        var attempts = 0;
        var states = new List<StartupState>();

        await startup.RunAsync(
            token =>
            {
                attempts++;
                states.Add(startup.State);
                return attempts == 1
                    ? Task.FromException<AppServices>(new StartupDependencyException("password authentication failed", canRetry: false, new IOException()))
                    : Task.FromResult(FakeServices());
            },
            _ => { }, NullLogger.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), CancellationToken.None,
            delay: (span, _) => { states.Add(startup.State); Assert.Equal(TimeSpan.FromSeconds(30), span); return Task.CompletedTask; });

        Assert.Contains(StartupState.Failed, states);
        Assert.True(startup.IsReady);
    }

    /// <summary>
    /// Verifies that any other error is not swallowed so the service still refuses to start.
    /// </summary>
    [Fact]
    public async Task Any_other_error_is_not_swallowed_so_the_service_still_refuses_to_start()
    {
        var startup = new AppStartup();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => startup.RunAsync(
            token => Task.FromException<AppServices>(new InvalidOperationException("Identity:Mode=Fixed is not allowed in production")),
            _ => { }, NullLogger.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), CancellationToken.None));

        Assert.Contains("not allowed in production", error.Message);
        Assert.False(startup.IsReady);
    }

    /// <summary>
    /// Verifies that shutdown stops the waiting.
    /// </summary>
    [Fact]
    public async Task Shutdown_stops_the_waiting()
    {
        var startup = new AppStartup();
        using var cts = new CancellationTokenSource();

        await startup.RunAsync(
            token =>
            {
                cts.Cancel();
                return Task.FromException<AppServices>(new StartupDependencyException("down", canRetry: true, new IOException()));
            },
            _ => { }, NullLogger.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), cts.Token);

        Assert.False(startup.IsReady);
        Assert.Equal(1, startup.Attempts);
    }

    /// <summary>
    /// Verifies that the waiting page is written to the log at most once a minute.
    /// </summary>
    [Fact]
    public void The_waiting_page_is_written_to_the_log_at_most_once_a_minute()
    {
        var startup = new AppStartup();
        var start = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

        Assert.True(startup.ShouldLogGateHit(start));
        Assert.False(startup.ShouldLogGateHit(start.AddSeconds(30)));
        Assert.True(startup.ShouldLogGateHit(start.AddSeconds(61)));
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = new List<string>();
        /// <summary>
        /// Test logger stub whose scope creation returns nothing, since the tests only inspect logged messages.
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>
        /// Test logger stub that reports a level as enabled.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>
        /// Test logger that records log entries in a list.
        /// </summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
