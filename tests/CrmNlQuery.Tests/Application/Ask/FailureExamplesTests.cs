using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask;

/// <summary>
/// Tests that admin example questions are appended to the could-not-understand messages, and left unchanged when there are none.
/// </summary>
public class FailureExamplesTests
{
    /// <summary>
    /// Verifies that with no examples the message is unchanged.
    /// </summary>
    [Fact]
    public void NoExamples_LeavesTheMessageUnchanged()
    {
        Assert.Equal(UserMessages.CannotAnswer, UserMessages.WithExamples(UserMessages.CannotAnswer, null));
        Assert.Equal(UserMessages.CannotAnswer, UserMessages.WithExamples(UserMessages.CannotAnswer, new List<string>()));
    }

    /// <summary>
    /// Verifies that examples are appended at most two.
    /// </summary>
    [Fact]
    public void Examples_AreAppended_AtMostTwo()
    {
        var text = UserMessages.WithExamples("Sorry.", new List<string> { "Show accounts in London", "List open opportunities", "Third one" });

        Assert.Equal("Sorry. You could ask, for example: \"Show accounts in London\" or \"List open opportunities\".", text);
    }

    private sealed class UnknownTableAnalyzer : IQuestionAnalyzer
    {
        /// <summary>
        /// Fake analyzer that reports an unknown table.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default)
            => Task.FromResult(new QuestionAnalysis("gizmo", new List<string>(), new List<Mention>(), new List<string>(), false));
    }

    private sealed class NoSearch : IRecordSearcher
    {
        /// <summary>
        /// Fake record searcher that finds nothing.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RecordMatch>>(new List<RecordMatch>());
    }

    private sealed class NoViews : IDefaultViewSource
    {
        /// <summary>
        /// Fake default-view source that returns no columns.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string>());
    }

    private sealed class NoPlanner : IPlanGenerator
    {
        /// <summary>
        /// Fake planner that is never expected to produce a plan.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoExecutor : IQueryExecutor
    {
        /// <summary>
        /// Fake executor that is never expected to run a query.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<string> WhichKindMessage(IReadOnlyList<string>? examples)
    {
        var pipeline = new AskPipeline(TestMetadata.Create(), new UnknownTableAnalyzer(), new NoSearch(), new ColumnPolicy(new NoViews()), new NoPlanner(), new NoExecutor(),
            exampleQuestions: examples);
        var outcome = await pipeline.RunAsync(new AskRequest("show gizmos", new Dictionary<string, string>(), Caller.ForEvaluation()));
        return Assert.IsType<QuestionOutcome>(outcome).Text;
    }

    /// <summary>
    /// Verifies that the which-kind-of-record message gets the configured examples.
    /// </summary>
    [Fact]
    public async Task WhichKindOfRecord_GetsTheConfiguredExamples()
    {
        var text = await WhichKindMessage(new List<string> { "Show accounts in London" });

        Assert.Contains("You could ask, for example: \"Show accounts in London\"", text);
    }

    /// <summary>
    /// Verifies that without examples the which-kind-of-record message is the old message.
    /// </summary>
    [Fact]
    public async Task WhichKindOfRecord_WithoutExamples_IsTheOldMessage()
    {
        var text = await WhichKindMessage(null);

        Assert.DoesNotContain("You could ask", text);
    }
}
