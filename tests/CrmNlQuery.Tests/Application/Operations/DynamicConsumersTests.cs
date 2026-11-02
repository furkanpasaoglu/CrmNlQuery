using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Knowledge.Models;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Application.Operations;

/// <summary>
/// Holds tests that panel-changed settings take effect in consumers without a restart.
/// </summary>
public class DynamicConsumersTests
{
    private static readonly AccessPrincipal Editor = new AccessPrincipal(@"contoso\editor", "Editor", new[] { "Editor" }, false, new[] { "Documents.Manage" });

    private sealed class CountingPlanner : IPlanGenerator
    {
        public int Calls { get; private set; }
        /// <summary>
        /// Test double planner that counts calls.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new PlanOutcome(true, "<fetch />", new List<string>()));
        }
    }

    private static PlanRequest Request() => new PlanRequest("contacts", new List<EntityInfo> { TestMetadata.Product() }, new List<ResolvedRecord>(), new List<string> { "name" }, false);

    /// <summary>
    /// Verifies that the plan cache follows aLive ttl and zero turns it off immediately.
    /// </summary>
    [Fact]
    public async Task ThePlanCache_FollowsALiveTtl_AndZeroTurnsItOffImmediately()
    {
        var inner = new CountingPlanner();
        var ttl = TimeSpan.FromHours(1);
        var cache = new CachingPlanGenerator(inner, () => ttl);
        await cache.GenerateAsync(Request());
        await cache.GenerateAsync(Request());
        Assert.Equal(1, inner.Calls);

        ttl = TimeSpan.Zero;
        await cache.GenerateAsync(Request());
        await cache.GenerateAsync(Request());

        Assert.Equal(3, inner.Calls);
    }

    /// <summary>
    /// Verifies that the plan cache starts working again when the ttl is raised from zero.
    /// </summary>
    [Fact]
    public async Task ThePlanCache_StartsWorkingAgain_WhenTheTtlIsRaisedFromZero()
    {
        var inner = new CountingPlanner();
        var ttl = TimeSpan.Zero;
        var cache = new CachingPlanGenerator(inner, () => ttl);
        await cache.GenerateAsync(Request());
        await cache.GenerateAsync(Request());
        ttl = TimeSpan.FromHours(1);

        await cache.GenerateAsync(Request());
        await cache.GenerateAsync(Request());

        Assert.Equal(3, inner.Calls);
    }

    /// <summary>
    /// Verifies that the knowledge switch and thresholds follow live options.
    /// </summary>
    [Fact]
    public async Task TheKnowledgeSwitch_AndThresholds_FollowLiveOptions()
    {
        var options = new KnowledgeOptions { Enabled = false, MinSimilarity = 0.2, TopK = 4 };
        var service = new KnowledgeService(new MemoryKnowledgeStore(), new FakeEmbeddings(), () => options, (e, c) => Task.CompletedTask);
        await service.CreateAsync(Editor, "KOBİ", "kobi müşteri segmenti iki");
        await service.CreateAsync(Editor, "Fatura", "kobi müşteri fatura vade");
        var retriever = new KnowledgeRetriever(service, () => options.Enabled, NullLogger.Instance);

        Assert.Empty(await retriever.FindAsync("kobi müşteri segmenti"));

        options = new KnowledgeOptions { Enabled = true, MinSimilarity = 0.2, TopK = 4 };
        Assert.Equal(2, (await retriever.FindAsync("kobi müşteri segmenti")).Count);

        options = new KnowledgeOptions { Enabled = true, MinSimilarity = 0.2, TopK = 1 };
        Assert.Single(await retriever.FindAsync("kobi müşteri segmenti"));

        options = new KnowledgeOptions { Enabled = false };
        Assert.Empty(await retriever.FindAsync("kobi müşteri segmenti"));
    }

    /// <summary>
    /// Verifies that the retention loop reads the days from the provider on every round.
    /// </summary>
    [Fact]
    public async Task TheRetentionLoop_ReadsTheDaysFromTheProviderOnEveryRound()
    {
        var days = 90;
        var seen = new List<double>();
        using var cts = new CancellationTokenSource();

        await MetricsRetention.RunAsync((cutoff, token) =>
        {
            seen.Add(Math.Round((DateTimeOffset.UtcNow - cutoff).TotalDays));
            cts.Cancel();
            return Task.FromResult(0);
        }, () => days, _ => { }, cts.Token);

        Assert.Equal(new[] { 90.0 }, seen);
    }

    /// <summary>
    /// Verifies that aRetention of zero days deletes nothing.
    /// </summary>
    [Fact]
    public async Task ARetentionOfZeroDays_DeletesNothing()
    {
        var purged = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await MetricsRetention.RunAsync((cutoff, token) => { purged++; return Task.FromResult(0); }, () => 0, _ => { }, cts.Token);

        Assert.Equal(0, purged);
    }
}
