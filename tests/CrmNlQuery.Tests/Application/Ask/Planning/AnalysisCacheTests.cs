using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Holds tests for the question analysis cache.
/// </summary>
public class AnalysisCacheTests
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();
    private static readonly IReadOnlyList<EntitySummary> Entities = new List<EntitySummary>
    {
        new EntitySummary("account", "accounts", "Account"), new EntitySummary("contact", "contacts", "Contact"),
    };
    private static readonly IReadOnlyList<string> NoNotes = new List<string>();

    private static string Key(Caller? caller = null, string question = "contact list of Contoso", IReadOnlyList<EntitySummary>? entities = null,
        IReadOnlyList<string>? knowledge = null, PreviousQuery? previous = null, ResolvedRecord? current = null)
        => AnalysisCache.KeyFor(caller ?? Caller.User(UserA), question, entities ?? Entities, knowledge ?? NoNotes, previous, current);

    private static QuestionAnalysis Analysis() => new QuestionAnalysis("contact", new List<string>(), new List<Mention>(), new List<string>(), false);

    /// <summary>
    /// Verifies that the same inputs give the same key even with other spacing and case.
    /// </summary>
    [Fact]
    public void TheSameInputs_GiveTheSameKey_EvenWithOtherSpacingAndCase()
    {
        Assert.Equal(Key(), Key());
        Assert.Equal(Key(question: "contact list of Contoso"), Key(question: "  Contact   list of   CONTOSO "));
    }

    /// <summary>
    /// Verifies that every input changes the key.
    /// </summary>
    [Fact]
    public void EveryInput_ChangesTheKey()
    {
        var baseKey = Key();
        Assert.NotEqual(baseKey, Key(caller: Caller.User(UserB)));
        Assert.NotEqual(baseKey, Key(question: "contact list of Fabrikam"));
        Assert.NotEqual(baseKey, Key(entities: Entities.Take(1).ToList()));
        Assert.NotEqual(baseKey, Key(entities: new List<EntitySummary> { Entities[0], new EntitySummary("contact", "contacts", "Person") }));
        Assert.NotEqual(baseKey, Key(knowledge: new List<string> { "A big opportunity is above 70,000." }));
        Assert.NotEqual(baseKey, Key(previous: new PreviousQuery("accounts in London", "account", "<fetch />")));
        Assert.NotEqual(baseKey, Key(current: new ResolvedRecord("this record", "account", Guid.NewGuid(), "Contoso")));
        Assert.NotEqual(Key(previous: new PreviousQuery("accounts in London", "account", "<fetch />")), Key(previous: new PreviousQuery("accounts in London", "account", "<fetch top=\"5\" />")));
        Assert.NotEqual(Key(current: new ResolvedRecord("this record", "account", Guid.Parse("00000000-0000-0000-0000-000000000001"), "Contoso")),
            Key(current: new ResolvedRecord("this record", "account", Guid.Parse("00000000-0000-0000-0000-000000000002"), "Contoso")));
    }

    /// <summary>
    /// Verifies that aRecorded analysis is returned until the time to live ends.
    /// </summary>
    [Fact]
    public void ARecordedAnalysis_IsReturned_UntilTheTimeToLiveEnds()
    {
        var now = DateTimeOffset.Parse("2026-10-20T10:00:00Z");
        var cache = new AnalysisCache(() => TimeSpan.FromMinutes(10), 100, () => now);
        var analysis = Analysis();
        cache.Put("k", analysis);

        Assert.True(cache.TryGet("k", out var found));
        Assert.Same(analysis, found);

        now = now.AddMinutes(9);
        Assert.True(cache.TryGet("k", out _));
        now = now.AddMinutes(2);
        Assert.False(cache.TryGet("k", out _));
    }

    /// <summary>
    /// Verifies that aTime to live of zero turns the cache off.
    /// </summary>
    [Fact]
    public void ATimeToLiveOfZero_TurnsTheCacheOff()
    {
        var cache = new AnalysisCache(() => TimeSpan.Zero);
        cache.Put("k", Analysis());

        Assert.False(cache.TryGet("k", out _));
    }

    /// <summary>
    /// Verifies that the oldest entry is dropped when the capacity is full.
    /// </summary>
    [Fact]
    public void TheOldestEntry_IsDropped_WhenTheCapacityIsFull()
    {
        var now = DateTimeOffset.Parse("2026-10-20T10:00:00Z");
        var cache = new AnalysisCache(() => TimeSpan.FromMinutes(10), 2, () => now);
        cache.Put("first", Analysis());
        now = now.AddSeconds(1);
        cache.Put("second", Analysis());
        now = now.AddSeconds(1);
        cache.Put("third", Analysis());

        Assert.False(cache.TryGet("first", out _));
        Assert.True(cache.TryGet("second", out _));
        Assert.True(cache.TryGet("third", out _));
    }

    /// <summary>
    /// Verifies that the evaluation caller has its own key but is still aValid key.
    /// </summary>
    [Fact]
    public void TheEvaluationCaller_HasItsOwnKey_ButIsStillAValidKey()
    {
        Assert.NotEqual(Key(caller: Caller.ForEvaluation()), Key(caller: Caller.User(UserA)));
    }
}
