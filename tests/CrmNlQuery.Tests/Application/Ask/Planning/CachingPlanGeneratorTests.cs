using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Tests the caching plan generator's keying, expiry, eviction and rule of never caching failed plans.
/// </summary>
public class CachingPlanGeneratorTests
{
    private sealed class CountingPlanner : IPlanGenerator
    {
        public int Calls { get; private set; }
        public Func<PlanRequest, PlanOutcome>? Respond { get; set; }

        /// <summary>
        /// Test fake planner that returns a canned plan outcome and counts calls.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Calls++;
            var outcome = Respond?.Invoke(request) ?? new PlanOutcome(true, $"<fetch n=\"{Calls}\" />", new List<string>());
            return Task.FromResult(outcome);
        }
    }

    private static EntityInfo Entity(params string[] attributes)
        => new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
            attributes.Select(a => new AttributeInfo(a, a, "String", new List<OptionInfo>())).ToList(), new List<RelationshipInfo>());

    private static PlanRequest Request(string question = "contacts of Brightwave", EntityInfo? entity = null,
        IReadOnlyList<ResolvedRecord>? resolved = null, IReadOnlyList<string>? columns = null, bool aggregate = false)
        => new PlanRequest(question, new[] { entity ?? Entity("fullname", "createdon") }, resolved ?? new List<ResolvedRecord>(),
            columns ?? new[] { "fullname" }, aggregate);

    private static ResolvedRecord Resolved(Guid id, string mention = "Brightwave") => new ResolvedRecord(mention, "account", id, mention + " Ltd");

    private static (CachingPlanGenerator Cache, CountingPlanner Inner) Make(TimeSpan? ttl = null, int capacity = 100, Func<DateTimeOffset>? clock = null)
    {
        var inner = new CountingPlanner();
        return (new CachingPlanGenerator(inner, ttl ?? TimeSpan.FromHours(1), capacity, clock), inner);
    }

    /// <summary>
    /// Verifies that changed company knowledge gives a different cache key so an old plan is not reused.
    /// </summary>
    [Fact]
    public async Task ChangedCompanyKnowledge_IsADifferentKey_SoAnOldPlanIsNotReused()
    {
        var (cache, inner) = Make();
        var before = Request() with { Knowledge = new[] { "KOBİ\nnew_segment = 2" } };
        var after = Request() with { Knowledge = new[] { "KOBİ\nnew_musteriSegmenti = 2" } };

        await cache.GenerateAsync(before);
        await cache.GenerateAsync(after);
        await cache.GenerateAsync(before);

        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that knowledge that disappears gives a different key than with knowledge.
    /// </summary>
    [Fact]
    public async Task KnowledgeThatDisappears_IsADifferentKey_ThanWithKnowledge()
    {
        var (cache, inner) = Make();

        await cache.GenerateAsync(Request() with { Knowledge = new[] { "KOBİ\nnew_segment = 2" } });
        await cache.GenerateAsync(Request());

        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that null and empty knowledge share the same key as before the knowledge base existed.
    /// </summary>
    [Fact]
    public async Task NullAndEmptyKnowledge_ShareTheSameKey_AsBeforeTheKnowledgeBaseExisted()
    {
        var (cache, inner) = Make();

        await cache.GenerateAsync(Request());
        await cache.GenerateAsync(Request() with { Knowledge = new List<string>() });

        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// Verifies that an identical request is served from the cache without calling the planner again.
    /// </summary>
    [Fact]
    public async Task AnIdenticalRequest_IsServedFromTheCache_WithoutCallingThePlannerAgain()
    {
        var (cache, inner) = Make();

        var first = await cache.GenerateAsync(Request());
        var second = await cache.GenerateAsync(Request());

        Assert.Equal(1, inner.Calls);
        Assert.Equal(first.FetchXml, second.FetchXml);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    /// <summary>
    /// Verifies that the question is compared ignoring case and extra spaces.
    /// </summary>
    [Fact]
    public async Task TheQuestion_IsComparedIgnoringCaseAndExtraSpaces()
    {
        var (cache, inner) = Make();

        await cache.GenerateAsync(Request("Contacts of  Brightwave"));
        await cache.GenerateAsync(Request("  contacts OF brightwave "));

        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// Verifies that questions differing in a meaningful detail such as a number use different cache entries.
    /// </summary>
    [Theory]
    [InlineData("contacts created in the last 30 days", "contacts created in the last 3 days")]
    [InlineData("opportunities above 1000", "opportunities above 10000")]
    public async Task QuestionsThatDifferInANumber_AreNeverTheSameEntry(string a, string b)
    {
        var (cache, inner) = Make();

        await cache.GenerateAsync(Request(a));
        await cache.GenerateAsync(Request(b));

        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that a different resolved record is a different cache entry.
    /// </summary>
    [Fact]
    public async Task ADifferentResolvedRecord_IsADifferentEntry()
    {
        var (cache, inner) = Make();

        await cache.GenerateAsync(Request(resolved: new[] { Resolved(Guid.NewGuid()) }));
        await cache.GenerateAsync(Request(resolved: new[] { Resolved(Guid.NewGuid()) }));

        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that the same resolved records in any order are the same entry.
    /// </summary>
    [Fact]
    public async Task TheSameResolvedRecords_InAnyOrder_AreTheSameEntry()
    {
        var (cache, inner) = Make();
        var a = Resolved(Guid.NewGuid(), "Alpha");
        var b = Resolved(Guid.NewGuid(), "Beta");

        await cache.GenerateAsync(Request(resolved: new[] { a, b }));
        await cache.GenerateAsync(Request(resolved: new[] { b, a }));

        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// Verifies that different columns or the aggregate flag are different cache entries.
    /// </summary>
    [Fact]
    public async Task DifferentColumns_OrTheAggregateFlag_AreDifferentEntries()
    {
        var (cache, inner) = Make();

        await cache.GenerateAsync(Request(columns: new[] { "fullname" }));
        await cache.GenerateAsync(Request(columns: new[] { "fullname", "emailaddress1" }));
        await cache.GenerateAsync(Request(aggregate: true));

        Assert.Equal(3, inner.Calls);
    }

    /// <summary>
    /// Verifies that when the metadata changes the old plan is not reused without any explicit refresh.
    /// </summary>
    [Fact]
    public async Task WhenTheMetadataChanges_TheOldPlanIsNotReused_WithoutAnyExplicitRefresh()
    {
        var (cache, inner) = Make();

        await cache.GenerateAsync(Request(entity: Entity("fullname", "createdon")));
        await cache.GenerateAsync(Request(entity: Entity("fullname", "createdon", "new_vipflag")));
        await cache.GenerateAsync(Request(entity: Entity("fullname", "createdon")));

        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that a changed option label counts as a metadata change.
    /// </summary>
    [Fact]
    public async Task AChangedOptionLabel_CountsAsAMetadataChange()
    {
        var (cache, inner) = Make();
        EntityInfo WithOption(string label) => new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
            new List<AttributeInfo> { new AttributeInfo("statuscode", "Status", "Picklist", new List<OptionInfo> { new OptionInfo(1, label) }) },
            new List<RelationshipInfo>());

        await cache.GenerateAsync(Request(entity: WithOption("Open")));
        await cache.GenerateAsync(Request(entity: WithOption("In progress")));

        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that a failed plan is never cached.
    /// </summary>
    [Fact]
    public async Task AFailedPlan_IsNeverCached()
    {
        var (cache, inner) = Make();
        inner.Respond = r => inner.Calls == 1
            ? new PlanOutcome(false, null, new List<string> { "bad" })
            : new PlanOutcome(true, "<fetch />", new List<string>());

        var first = await cache.GenerateAsync(Request());
        var second = await cache.GenerateAsync(Request());

        Assert.False(first.Success);
        Assert.True(second.Success);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that a planner exception propagates and caches nothing.
    /// </summary>
    [Fact]
    public async Task APlannerException_PropagatesAndCachesNothing()
    {
        var inner = new ThrowingOncePlanner();
        var cache = new CachingPlanGenerator(inner, TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GenerateAsync(Request()));
        var result = await cache.GenerateAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that an entry expires after the time to live.
    /// </summary>
    [Fact]
    public async Task AnEntry_ExpiresAfterTheTimeToLive()
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var (cache, inner) = Make(TimeSpan.FromMinutes(10), clock: () => now);

        await cache.GenerateAsync(Request());
        now = now.AddMinutes(9);
        await cache.GenerateAsync(Request());
        now = now.AddMinutes(2);
        await cache.GenerateAsync(Request());

        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that when the cache is full the oldest entry is dropped.
    /// </summary>
    [Fact]
    public async Task WhenFull_TheOldestEntryIsDropped()
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var (cache, inner) = Make(capacity: 2, clock: () => now);

        await cache.GenerateAsync(Request("one")); now = now.AddSeconds(1);
        await cache.GenerateAsync(Request("two")); now = now.AddSeconds(1);
        await cache.GenerateAsync(Request("three")); now = now.AddSeconds(1);
        Assert.Equal(3, inner.Calls);

        await cache.GenerateAsync(Request("two"));
        await cache.GenerateAsync(Request("three"));
        Assert.Equal(3, inner.Calls);

        await cache.GenerateAsync(Request("one"));
        Assert.Equal(4, inner.Calls);
    }

    private sealed class ThrowingOncePlanner : IPlanGenerator
    {
        public int Calls { get; private set; }

        /// <summary>
        /// Test fake planner that returns a canned plan outcome.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Calls++;
            if (Calls == 1)
            {
                throw new InvalidOperationException("model down");
            }
            return Task.FromResult(new PlanOutcome(true, "<fetch />", new List<string>()));
        }
    }
}
