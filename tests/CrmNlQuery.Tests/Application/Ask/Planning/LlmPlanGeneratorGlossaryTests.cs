using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Glossary.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Unit tests for the glossary in the plan generator: prompt contents, enforcement with retry and explicit failure, and cache key.
/// </summary>
public class LlmPlanGeneratorGlossaryTests
{
    private static string Plan(string condition) => "{\"steps\":[{\"fetchXml\":\"<fetch top=\\\"500\\\"><entity name=\\\"account\\\"><attribute name=\\\"name\\\" /><filter>" + condition.Replace("\"", "\\\"") + "</filter></entity></fetch>\"}]}";

    private const string ActiveCondition = "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" />";
    private const string CityCondition = "<condition attribute=\"address1_city\" operator=\"like\" value=\"%Ankara%\" />";

    private static GlossaryHit ActiveFirms(DateTimeOffset? modified = null) => new GlossaryHit(new GlossaryEntry
    {
        Id = "g1", Kind = GlossaryKind.Filter, Entity = "account", Terms = new[] { new GlossaryTerm("aktif firma") },
        Conditions = new[] { new GlossaryCondition("statecode", "eq", new[] { "0" }) }, Description = "only active accounts", ModifiedAt = modified ?? DateTimeOffset.UnixEpoch,
    }, "aktif firma");

    private static PlanRequest Request(IReadOnlyList<GlossaryHit>? glossary = null) => new PlanRequest("aktif firmaları getir",
        new List<EntityInfo> { TestMetadata.Account() }, new List<ResolvedRecord>(), new List<string> { "name" }, false, Glossary: glossary);

    private static LlmPlanGenerator Generator(FakeLlm llm) => new LlmPlanGenerator(llm, new PlanValidator(TestMetadata.Create()));

    /// <summary>
    /// Verifies that without glossary hits the prompt is exactly the old one.
    /// </summary>
    [Fact]
    public async Task WithoutGlossaryHits_ThePromptIsExactlyTheOldOne()
    {
        var withNull = new FakeLlm(Plan(CityCondition));
        var withEmpty = new FakeLlm(Plan(CityCondition));

        await Generator(withNull).GenerateAsync(Request(null));
        await Generator(withEmpty).GenerateAsync(Request(new List<GlossaryHit>()));

        Assert.Equal(withNull.Calls[0].User, withEmpty.Calls[0].User);
        Assert.DoesNotContain("Business glossary", withNull.Calls[0].User);
    }

    /// <summary>
    /// Verifies that the glossary block is in the prompt just before the question.
    /// </summary>
    [Fact]
    public async Task TheGlossaryBlock_IsInThePrompt_JustBeforeTheQuestion()
    {
        var llm = new FakeLlm(Plan(ActiveCondition));

        var outcome = await Generator(llm).GenerateAsync(Request(new[] { ActiveFirms() }));

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        var user = llm.Calls[0].User;
        var glossaryAt = user.IndexOf("Business glossary", StringComparison.Ordinal);
        var questionAt = user.IndexOf("Question: ", StringComparison.Ordinal);
        Assert.True(glossaryAt > 0 && glossaryAt < questionAt);
        Assert.Contains("statecode eq 0", user);
        Assert.Contains("only active accounts", user);
    }

    /// <summary>
    /// Verifies that a model that forgets the definition is told exactly what to add and the retry is applied.
    /// </summary>
    [Fact]
    public async Task AModelThatForgetsTheDefinition_IsToldExactlyWhatToAdd_AndTheRetryIsApplied()
    {
        var llm = new FakeLlm(Plan(CityCondition), Plan(CityCondition + ActiveCondition));

        var outcome = await Generator(llm).GenerateAsync(Request(new[] { ActiveFirms() }));

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.Equal(2, llm.Calls.Count);
        Assert.Contains("glossary term 'aktif firma' requires these conditions on account: statecode eq 0", llm.Calls[1].User);
        var finding = Assert.Single(outcome.Glossary);
        Assert.True(finding.Applied);
    }

    /// <summary>
    /// Verifies that a model that never applies it produces a failure with the not applied finding and no query to run.
    /// </summary>
    [Fact]
    public async Task AModelThatNeverAppliesIt_ProducesAFailure_WithTheNotAppliedFinding_AndNoQueryToRun()
    {
        var llm = new FakeLlm(Plan(CityCondition), Plan(CityCondition), Plan(CityCondition), Plan(CityCondition));

        var outcome = await Generator(llm).GenerateAsync(Request(new[] { ActiveFirms() }));

        Assert.False(outcome.Success);
        Assert.Null(outcome.FetchXml);
        Assert.Contains(outcome.Errors, e => e.Contains(GlossaryEnforcer.Marker));
        var finding = Assert.Single(outcome.Glossary);
        Assert.False(finding.Applied);
    }

    /// <summary>
    /// Verifies that a request without glossary never gets a glossary error.
    /// </summary>
    [Fact]
    public async Task ARequestWithoutGlossary_NeverGetsAGlossaryError()
    {
        var llm = new FakeLlm(Plan(CityCondition));

        var outcome = await Generator(llm).GenerateAsync(Request());

        Assert.True(outcome.Success);
        Assert.Empty(outcome.Glossary);
    }

    private sealed class CountingPlanner : IPlanGenerator
    {
        public int Calls { get; private set; }

        /// <summary>
        /// Test double that generate with canned data instead of calling a real dependency.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new PlanOutcome(true, $"<fetch n=\"{Calls}\" />", new List<string>()));
        }
    }

    /// <summary>
    /// Verifies that a changed glossary entry is a different cache key.
    /// </summary>
    [Fact]
    public async Task AChangedGlossaryEntry_IsADifferentCacheKey()
    {
        var inner = new CountingPlanner();
        var cache = new CachingPlanGenerator(inner, TimeSpan.FromHours(1), 100, null);
        var first = Request(new[] { ActiveFirms(DateTimeOffset.UnixEpoch) });
        var changed = Request(new[] { ActiveFirms(DateTimeOffset.UnixEpoch.AddMinutes(5)) });

        await cache.GenerateAsync(first);
        await cache.GenerateAsync(first);
        await cache.GenerateAsync(changed);
        await cache.GenerateAsync(Request());

        Assert.Equal(3, inner.Calls);
    }

    /// <summary>
    /// Verifies that the analysis cache key changes when the glossary changes.
    /// </summary>
    [Fact]
    public void TheAnalysisCacheKey_ChangesWhenTheGlossaryChanges()
    {
        var caller = CrmNlQuery.Domain.Access.Entities.Caller.ForEvaluation();
        var entities = new List<EntitySummary> { new EntitySummary("account", "accounts", "Account") };

        string Key(params GlossaryHit[] hits) => AnalysisCache.KeyFor(caller, "q", entities, new List<string>(), null, null, hits);

        Assert.Equal(Key(), Key());
        Assert.NotEqual(Key(), Key(ActiveFirms()));
        Assert.NotEqual(Key(ActiveFirms(DateTimeOffset.UnixEpoch)), Key(ActiveFirms(DateTimeOffset.UnixEpoch.AddMinutes(1))));
    }
}
