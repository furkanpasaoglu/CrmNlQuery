using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Tests that the model answers cannotAnswer instead of substituting another field for a closed or missing one.
/// </summary>
public class LlmPlanGeneratorCannotAnswerTests
{
    private static PlanRequest Request() => new PlanRequest("products born before 1980",
        new List<EntityInfo> { TestMetadata.Product() }, new List<ResolvedRecord>(), new List<string> { "name" }, false);

    private static LlmPlanGenerator Generator(FakeLlm llm) => new LlmPlanGenerator(llm, new PlanValidator(TestMetadata.Create()));

    /// <summary>
    /// Verifies that the flag stops at once without retries and is not a success.
    /// </summary>
    [Fact]
    public async Task TheFlag_StopsAtOnce_WithoutRetries_AndIsNotASuccess()
    {
        var llm = new FakeLlm("{\"cannotAnswer\":true,\"steps\":[]}");

        var outcome = await Generator(llm).GenerateAsync(Request());

        Assert.True(outcome.CannotAnswer);
        Assert.False(outcome.Success);
        Assert.Single(llm.Calls);
    }

    /// <summary>
    /// Verifies that the rule is in the prompt and a false flag uses the plan.
    /// </summary>
    [Fact]
    public async Task TheRule_IsInThePrompt_AndAFalseFlagUsesThePlan()
    {
        var plan = "{\"cannotAnswer\":false,\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"product\\\"><attribute name=\\\"name\\\" /></entity></fetch>\"}]}";
        var llm = new FakeLlm(plan);

        var outcome = await Generator(llm).GenerateAsync(Request());

        Assert.Contains("Never substitute a different attribute", llm.Calls[0].System);
        Assert.True(outcome.Success);
        Assert.False(outcome.CannotAnswer);
    }
}
