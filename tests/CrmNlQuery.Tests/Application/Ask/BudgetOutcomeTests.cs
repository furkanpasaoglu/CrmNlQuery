using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Ask;

/// <summary>
/// Holds tests for the outcome when the cloud token budget is exceeded.
/// </summary>
public class BudgetOutcomeTests
{
    private sealed class OverBudgetAnalyzer : IQuestionAnalyzer
    {
        /// <summary>
        /// Test double analyzer that behaves as if over budget.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default)
            => throw new BudgetExceededException("The daily cloud token budget (1000) has been reached.");
    }

    private sealed class NoSearch : IRecordSearcher
    {
        /// <summary>
        /// Test double record search that finds nothing.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RecordMatch>>(new List<RecordMatch>());
    }

    private sealed class NoViews : IDefaultViewSource
    {
        /// <summary>
        /// Test double view source that returns no default columns.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string>());
    }

    private sealed class NoPlanner : IPlanGenerator
    {
        /// <summary>
        /// Test double planner that must not be reached.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoExecutor : IQueryExecutor
    {
        /// <summary>
        /// Test double executor that must not be reached.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Verifies that when the cloud budget is used up the user gets aFixed message and aStable kind.
    /// </summary>
    [Fact]
    public async Task WhenTheCloudBudgetIsUsedUp_TheUserGetsAFixedMessage_AndAStableKind()
    {
        var pipeline = new AskPipeline(TestMetadata.Create(), new OverBudgetAnalyzer(), new NoSearch(), new ColumnPolicy(new NoViews()), new NoPlanner(), new NoExecutor());

        var outcome = await pipeline.RunAsync(new AskRequest("all products", new Dictionary<string, string>(), Caller.ForEvaluation()));

        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("budget_exceeded", failure.Kind);
        Assert.Equal(UserMessages.BudgetExceeded, failure.Message);
        Assert.DoesNotContain("1000", failure.Message);
    }
}
