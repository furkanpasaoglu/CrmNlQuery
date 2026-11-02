using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Tests.Support;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Tests the question decomposer's parsing, retry and fallback behaviour.
/// </summary>
public class LlmQuestionDecomposerTests
{
    private const string OneStep = """{"steps":[{"question":"all products","condition":"always"}]}""";

    private const string TwoSteps =
        """{"steps":[{"question":"contracts of Denema","condition":"always"},{"question":"contracts of Ornekova","condition":"if_previous_empty"}]}""";

    /// <summary>
    /// Verifies that a single step is parsed.
    /// </summary>
    [Fact]
    public async Task SingleStep_IsParsed()
    {
        var steps = await new LlmQuestionDecomposer(new FakeLlm(OneStep)).DecomposeAsync("all products");
        var step = Assert.Single(steps);
        Assert.Equal("all products", step.Question);
        Assert.Equal(StepCondition.Always, step.Condition);
    }

    /// <summary>
    /// Verifies that two steps are parsed with their conditions.
    /// </summary>
    [Fact]
    public async Task TwoSteps_AreParsedWithConditions()
    {
        var steps = await new LlmQuestionDecomposer(new FakeLlm(TwoSteps)).DecomposeAsync("q");
        Assert.Equal(2, steps.Count);
        Assert.Equal(StepCondition.Always, steps[0].Condition);
        Assert.Equal(StepCondition.IfPreviousEmpty, steps[1].Condition);
        Assert.Equal("contracts of Ornekova", steps[1].Question);
    }

    /// <summary>
    /// Verifies that an if-previous-not-empty condition is parsed.
    /// </summary>
    [Fact]
    public async Task IfPreviousNotEmpty_IsParsed()
    {
        var reply = """{"steps":[{"question":"a","condition":"always"},{"question":"b","condition":"if_previous_not_empty"}]}""";
        var steps = await new LlmQuestionDecomposer(new FakeLlm(reply)).DecomposeAsync("q");
        Assert.Equal(StepCondition.IfPreviousNotEmpty, steps[1].Condition);
    }

    /// <summary>
    /// Verifies that the user question is sent to the model.
    /// </summary>
    [Fact]
    public async Task UserQuestion_IsSentToTheModel()
    {
        var llm = new FakeLlm(OneStep);
        await new LlmQuestionDecomposer(llm).DecomposeAsync("Denema contracts please");
        Assert.Contains("Denema contracts please", llm.Calls[0].User);
    }

    /// <summary>
    /// Verifies that a first step with a condition is rejected and then retried.
    /// </summary>
    [Fact]
    public async Task FirstStepWithCondition_IsRejected_ThenRetried()
    {
        var bad = """{"steps":[{"question":"a","condition":"if_previous_empty"}]}""";
        var llm = new FakeLlm(bad, TwoSteps);
        var steps = await new LlmQuestionDecomposer(llm).DecomposeAsync("q");
        Assert.Equal(2, steps.Count);
        Assert.Equal(2, llm.Calls.Count);
        Assert.Contains("not usable", llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that too many steps fall back to a single step with the original question.
    /// </summary>
    [Fact]
    public async Task TooManySteps_FallBackToSingleStepWithOriginalQuestion()
    {
        var four = """{"steps":[{"question":"a","condition":"always"},{"question":"b","condition":"if_previous_empty"},{"question":"c","condition":"if_previous_empty"},{"question":"d","condition":"if_previous_empty"}]}""";
        var llm = new FakeLlm(four, four);
        var steps = await new LlmQuestionDecomposer(llm).DecomposeAsync("original question");
        var step = Assert.Single(steps);
        Assert.Equal("original question", step.Question);
        Assert.Equal(StepCondition.Always, step.Condition);
        Assert.Equal(2, llm.Calls.Count);
    }

    /// <summary>
    /// Verifies that a blank step question falls back to the original.
    /// </summary>
    [Fact]
    public async Task BlankStepQuestion_FallsBackToOriginal()
    {
        var blank = """{"steps":[{"question":"  ","condition":"always"}]}""";
        var steps = await new LlmQuestionDecomposer(new FakeLlm(blank, blank)).DecomposeAsync("original question");
        Assert.Equal("original question", Assert.Single(steps).Question);
    }

    /// <summary>
    /// Verifies that an unknown condition falls back to the original.
    /// </summary>
    [Fact]
    public async Task UnknownCondition_FallsBackToOriginal()
    {
        var odd = """{"steps":[{"question":"a","condition":"always"},{"question":"b","condition":"whenever"}]}""";
        var steps = await new LlmQuestionDecomposer(new FakeLlm(odd, odd)).DecomposeAsync("original question");
        Assert.Equal("original question", Assert.Single(steps).Question);
    }

    /// <summary>
    /// Verifies that non-JSON output falls back to the original.
    /// </summary>
    [Fact]
    public async Task NotJson_FallsBackToOriginal()
    {
        var steps = await new LlmQuestionDecomposer(new FakeLlm("nonsense", "")).DecomposeAsync("original question");
        Assert.Equal("original question", Assert.Single(steps).Question);
    }

    /// <summary>
    /// Verifies that an empty step list falls back to the original.
    /// </summary>
    [Fact]
    public async Task EmptyStepList_FallsBackToOriginal()
    {
        var empty = """{"steps":[]}""";
        var steps = await new LlmQuestionDecomposer(new FakeLlm(empty, empty)).DecomposeAsync("original question");
        Assert.Equal("original question", Assert.Single(steps).Question);
    }
}
