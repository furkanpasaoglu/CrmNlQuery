using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Tests the needs-steps flag, where the single-step planner says one query is not enough.
/// </summary>
public class LlmPlanGeneratorNeedsStepsTests
{
    private static PlanRequest Request() => new PlanRequest("contacts of the account with the most opportunities",
        new List<EntityInfo> { TestMetadata.Product() }, new List<ResolvedRecord>(), new List<string> { "name" }, false);

    private static LlmPlanGenerator Generator(FakeLlm llm) => new LlmPlanGenerator(llm, new PlanValidator(TestMetadata.Create()));

    /// <summary>
    /// Verifies that the flag stops at once without retries and is not a success.
    /// </summary>
    [Fact]
    public async Task TheFlag_StopsAtOnce_WithoutRetries_AndIsNotASuccess()
    {
        var llm = new FakeLlm("{\"needsSteps\":true,\"steps\":[]}");

        var outcome = await Generator(llm).GenerateAsync(Request());

        Assert.True(outcome.NeedsSteps);
        Assert.False(outcome.Success);
        Assert.Single(llm.Calls);
    }

    /// <summary>
    /// Verifies that inside a chain step the flag is not offered and is ignored if the model sends it.
    /// </summary>
    [Fact]
    public async Task InsideAChainStep_TheFlagIsNotOffered_AndIsIgnoredIfTheModelSendsIt()
    {
        var plan = "{\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"product\\\"><attribute name=\\\"name\\\" /></entity></fetch>\"}]}";
        var llm = new FakeLlm("{\"needsSteps\":true,\"steps\":[]}", plan);
        var request = Request() with { Step = new StepPlanInfo(null, new string[0], true) };

        var outcome = await Generator(llm).GenerateAsync(request);

        Assert.DoesNotContain("needsSteps", llm.Calls[0].System);
        Assert.False(outcome.NeedsSteps);
        Assert.True(outcome.Success);
        Assert.Equal(2, llm.Calls.Count);
    }

    /// <summary>
    /// Verifies that a normal plan is never flagged.
    /// </summary>
    [Fact]
    public async Task ANormalPlan_IsNeverFlagged()
    {
        var plan = "{\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"product\\\"><attribute name=\\\"name\\\" /></entity></fetch>\"}]}";

        var outcome = await Generator(new FakeLlm(plan)).GenerateAsync(Request());

        Assert.True(outcome.Success);
        Assert.False(outcome.NeedsSteps);
    }

    /// <summary>
    /// Verifies that a false flag is ignored and the plan is used.
    /// </summary>
    [Fact]
    public async Task AFalseFlag_IsIgnored_AndThePlanIsUsed()
    {
        var plan = "{\"needsSteps\":false,\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"product\\\"><attribute name=\\\"name\\\" /></entity></fetch>\"}]}";

        var outcome = await Generator(new FakeLlm(plan)).GenerateAsync(Request());

        Assert.True(outcome.Success);
    }

    /// <summary>
    /// Verifies that broken JSON is still retried as before and not treated as the flag.
    /// </summary>
    [Fact]
    public async Task BrokenJson_IsStillRetriedAsBefore_NotTreatedAsTheFlag()
    {
        var llm = new FakeLlm("garbage", "{", "nope");

        var outcome = await Generator(llm).GenerateAsync(Request());

        Assert.False(outcome.NeedsSteps);
        Assert.Equal(LlmPlanGenerator.MaxAttempts, llm.Calls.Count);
    }

    /// <summary>
    /// Verifies that the prompt tells the model when to use the flag.
    /// </summary>
    [Fact]
    public async Task ThePrompt_TellsTheModelWhenToUseTheFlag()
    {
        var llm = new FakeLlm("{\"needsSteps\":true,\"steps\":[]}");

        await Generator(llm).GenerateAsync(Request());

        Assert.Contains("needsSteps", llm.Calls[0].System);
        Assert.Contains("ONE query", llm.Calls[0].System);
    }
}
