using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Tests.Support;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Holds tests for the decomposer returning dependent steps.
/// </summary>
public class LlmQuestionDecomposerDependencyTests
{
    private const string Dependent =
        """{"steps":[{"question":"the account with the most open opportunities","condition":"always","usesStep":0},{"question":"the contacts of those accounts","condition":"always","usesStep":1}]}""";

    /// <summary>
    /// Verifies that aDependent step carries the step it uses and the first step carries none.
    /// </summary>
    [Fact]
    public async Task ADependentStep_CarriesTheStepItUses_AndTheFirstStepCarriesNone()
    {
        var steps = await new LlmQuestionDecomposer(new FakeLlm(Dependent)).DecomposeAsync("q");

        Assert.Equal(2, steps.Count);
        Assert.Null(steps[0].UsesStep);
        Assert.Equal(1, steps[1].UsesStep);
        Assert.Equal("the contacts of those accounts", steps[1].Question);
    }

    /// <summary>
    /// Verifies that when uses step is missing the step is independent.
    /// </summary>
    [Fact]
    public async Task WhenUsesStepIsMissing_TheStepIsIndependent()
    {
        var reply = """{"steps":[{"question":"a","condition":"always"},{"question":"b","condition":"always"}]}""";

        var steps = await new LlmQuestionDecomposer(new FakeLlm(reply)).DecomposeAsync("q");

        Assert.All(steps, s => Assert.Null(s.UsesStep));
    }

    /// <summary>
    /// Verifies that an invalid dependency is rejected then retried then falls back to one step.
    /// </summary>
    [Theory]
    [InlineData("""{"steps":[{"question":"a","condition":"always","usesStep":1}]}""")]
    [InlineData("""{"steps":[{"question":"a","condition":"always"},{"question":"b","condition":"always","usesStep":2}]}""")]
    [InlineData("""{"steps":[{"question":"a","condition":"always"},{"question":"b","condition":"always","usesStep":3}]}""")]
    [InlineData("""{"steps":[{"question":"a","condition":"always"},{"question":"b","condition":"always","usesStep":-1}]}""")]
    [InlineData("""{"steps":[{"question":"a","condition":"always"},{"question":"b","condition":"if_previous_empty","usesStep":1}]}""")]
    public async Task AnInvalidDependency_IsRejected_ThenRetried_ThenFallsBackToOneStep(string bad)
    {
        var llm = new FakeLlm(bad, bad);

        var steps = await new LlmQuestionDecomposer(llm).DecomposeAsync("the original question");

        var only = Assert.Single(steps);
        Assert.Equal("the original question", only.Question);
        Assert.Equal(2, llm.Calls.Count);
        Assert.Contains("not usable", llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that three step chain can use the earlier steps in order.
    /// </summary>
    [Fact]
    public async Task ThreeStepChain_CanUseTheEarlierStepsInOrder()
    {
        var reply = """{"steps":[{"question":"a","condition":"always"},{"question":"b","condition":"always","usesStep":1},{"question":"c","condition":"always","usesStep":2}]}""";

        var steps = await new LlmQuestionDecomposer(new FakeLlm(reply)).DecomposeAsync("q");

        Assert.Equal(new int?[] { null, 1, 2 }, steps.Select(s => s.UsesStep).ToArray());
    }

    /// <summary>
    /// Verifies that the prompt explains dependent steps and when not to split.
    /// </summary>
    [Fact]
    public async Task ThePrompt_ExplainsDependentStepsAndWhenNotToSplit()
    {
        var llm = new FakeLlm(Dependent);

        await new LlmQuestionDecomposer(llm).DecomposeAsync("q");

        var system = llm.Calls[0].System;
        Assert.Contains("usesStep", system);
        Assert.Contains("ONE step", system);
        Assert.Contains("those", system);
    }
}
