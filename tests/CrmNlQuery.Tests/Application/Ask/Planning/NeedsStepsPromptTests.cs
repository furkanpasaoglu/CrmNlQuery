using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Tests.Support;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Tests the planner prompt wording around when to split a question into steps.
/// </summary>
public class NeedsStepsPromptTests
{
    /// <summary>
    /// Verifies that the prompt says counting per group is one aggregate query and not a reason to split.
    /// </summary>
    [Fact]
    public async Task ThePrompt_SaysCountingPerGroupIsOneAggregateQuery_NotAReasonToSplit()
    {
        var llm = new FakeLlm("{\"needsSteps\":true,\"steps\":[]}");
        var request = new CrmNlQuery.Domain.Ask.Entities.PlanRequest("q", new List<CrmNlQuery.Domain.Metadata.Entities.EntityInfo> { CrmNlQuery.Tests.Support.TestMetadata.Product() },
            new List<CrmNlQuery.Domain.Ask.Entities.ResolvedRecord>(), new List<string> { "name" }, true);

        await new LlmPlanGenerator(llm, new CrmNlQuery.Application.Ask.Planning.Validation.PlanValidator(CrmNlQuery.Tests.Support.TestMetadata.Create())).GenerateAsync(request);

        Assert.Contains("EACH account", llm.Calls[0].System);
    }
}
