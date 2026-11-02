using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask;

/// <summary>
/// Tests that the question pipeline uses the open form record only when the analyzer says the question refers to it.
/// </summary>
public class FormContextPipelineTests
{
    private static readonly ResolvedRecord Open = new ResolvedRecord("this record", "account", Guid.Parse("11111111-1111-1111-1111-111111111111"), "Brightwave");

    private sealed class ContextAnalyzer : IQuestionAnalyzer
    {
        private readonly QuestionAnalysis _result;
        public ResolvedRecord? SeenCurrent { get; private set; }
        public ContextAnalyzer(QuestionAnalysis result) { _result = result; }
        /// <summary>
        /// Fake analyzer that reports whether the question refers to the open record.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default) => Task.FromResult(_result);
        /// <summary>
        /// Fake analyzer overload that reports a reference to the current record.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, PreviousQuery? previous, ResolvedRecord? current, CancellationToken ct = default)
        {
            SeenCurrent = current;
            return Task.FromResult(_result);
        }
    }

    private sealed class CapturingPlanner : IPlanGenerator
    {
        public PlanRequest? Last { get; private set; }
        /// <summary>
        /// Fake planner that captures the plan request it receives.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Last = request;
            return Task.FromResult(new PlanOutcome(true, "<fetch top=\"500\"><entity name=\"contract\"><attribute name=\"title\" /></entity></fetch>", new List<string>()));
        }
    }

    private sealed class ConstantExecutor : IQueryExecutor
    {
        /// <summary>
        /// Fake executor that returns a constant result table for any query.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
            => Task.FromResult(new ResultTable(new List<string> { "title" }, new List<IReadOnlyList<string>> { new List<string> { "x" } }));
    }

    private sealed class Views : IDefaultViewSource
    {
        /// <summary>
        /// Fake view source that returns no default columns.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string> { "title" });
    }

    private sealed class NoSearch : IRecordSearcher
    {
        /// <summary>
        /// Fake record searcher that never finds any record.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RecordMatch>>(new List<RecordMatch>());
    }

    private static QuestionAnalysis Analysis(bool refersToOpenRecord)
        => new QuestionAnalysis("contract", new List<string>(), new List<Mention>(), new List<string>(), false, false, refersToOpenRecord);

    private static async Task<(AskOutcome Outcome, CapturingPlanner Planner, ContextAnalyzer Analyzer)> Run(bool refers, ResolvedRecord? current)
    {
        var planner = new CapturingPlanner();
        var analyzer = new ContextAnalyzer(Analysis(refers));
        var pipeline = new AskPipeline(TestMetadata.Create(), analyzer, new NoSearch(), new ColumnPolicy(new Views()), planner, new ConstantExecutor(),
            decomposer: null, logger: null, knowledge: null);
        var request = new AskRequest("bu müşterinin contractları", new Dictionary<string, string> { ["columns"] = "default" }, Caller.ForEvaluation(), null, CurrentRecord: current);
        var outcome = await pipeline.RunAsync(request);
        return (outcome, planner, analyzer);
    }

    /// <summary>
    /// Verifies that when the question points at the open record it goes to the planner as a resolved record.
    /// </summary>
    [Fact]
    public async Task WhenTheQuestionPointsAtTheOpenRecord_ItGoesToThePlannerAsAResolvedRecord()
    {
        var (outcome, planner, analyzer) = await Run(true, Open);

        var answer = Assert.IsType<AnswerOutcome>(outcome);
        Assert.Same(Open, analyzer.SeenCurrent);
        Assert.Contains(planner.Last!.Resolved, r => r.Id == Open.Id && r.Entity == "account");
        Assert.Contains(planner.Last.Entities, e => e.LogicalName == "account");
        Assert.Equal("Brightwave", answer.AboutRecord);
    }

    /// <summary>
    /// Verifies that when the question does not point at it the open record is dropped.
    /// </summary>
    [Fact]
    public async Task WhenTheQuestionDoesNotPointAtIt_TheOpenRecordIsDropped()
    {
        var (outcome, planner, _) = await Run(false, Open);

        var answer = Assert.IsType<AnswerOutcome>(outcome);
        Assert.Empty(planner.Last!.Resolved);
        Assert.Null(answer.AboutRecord);
    }

    /// <summary>
    /// Verifies that without an open record nothing changes even if the model says it points at one.
    /// </summary>
    [Fact]
    public async Task WithoutAnOpenRecord_NothingChanges_EvenIfTheModelSaysItPointsAtOne()
    {
        var (outcome, planner, analyzer) = await Run(true, null);

        Assert.Null(analyzer.SeenCurrent);
        Assert.Empty(planner.Last!.Resolved);
        Assert.Null(Assert.IsType<AnswerOutcome>(outcome).AboutRecord);
    }
}
