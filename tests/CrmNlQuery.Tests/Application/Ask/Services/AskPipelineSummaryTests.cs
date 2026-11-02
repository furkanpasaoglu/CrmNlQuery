using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Summary.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Services;

/// <summary>
/// Tests that a summary word plus one resolved account or contact yields a customer summary without the model or planner.
/// </summary>
public class AskPipelineSummaryTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Analyzer : IQuestionAnalyzer
    {
        private readonly QuestionAnalysis _analysis;
        public Analyzer(QuestionAnalysis analysis) { _analysis = analysis; }
        /// <summary>
        /// Fake analyzer returning a canned question analysis.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default) => Task.FromResult(_analysis);
    }

    private sealed class Searcher : IRecordSearcher
    {
        private readonly Dictionary<string, RecordMatch> _byText;
        public Searcher(Dictionary<string, RecordMatch> byText) { _byText = byText; }
        /// <summary>
        /// Fake record searcher returning canned matches.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
        {
            IReadOnlyList<RecordMatch> list = _byText.TryGetValue(text, out var found) ? new List<RecordMatch> { found } : new List<RecordMatch>();
            return Task.FromResult(list);
        }
    }

    private sealed class Views : IDefaultViewSource
    {
        /// <summary>
        /// Fake default-view source returning canned columns.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "name" });
    }

    private sealed class Planner : IPlanGenerator
    {
        public int Calls;
        /// <summary>
        /// Fake planner for the summary pipeline tests.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new PlanOutcome(true, "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>", new List<string>()));
        }
    }

    private sealed class Executor : IQueryExecutor
    {
        public readonly List<Caller> Callers = new List<Caller>();
        /// <summary>
        /// Fake executor for the summary pipeline tests.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            lock (Callers) { Callers.Add(caller); }
            var aggregate = fetchXml.Contains("aggregate=\"true\"");
            return Task.FromResult(aggregate
                ? new ResultTable(new[] { "n" }, new List<IReadOnlyList<string>> { new[] { "1" } })
                : new ResultTable(new[] { "name" }, new List<IReadOnlyList<string>> { new[] { "Brightwave" } }));
        }
    }

    private static AskPipeline Build(QuestionAnalysis analysis, Planner planner, Executor executor, bool summaryOn = true, string company = "Brightwave")
    {
        var metadata = TestMetadata.Create();
        var summary = new SummaryBuilder(metadata, executor, new Views());
        var searcher = new Searcher(new Dictionary<string, RecordMatch> { [company] = new RecordMatch(AccountId, "Brightwave Appliances Ltd") });
        return new AskPipeline(metadata, new Analyzer(analysis), searcher, new ColumnPolicy(new Views()), planner, executor,
            summary: summary, summaryEnabled: () => summaryOn);
    }

    private static AskRequest Ask(string question, ResolvedRecord? current = null)
        => new AskRequest(question, new Dictionary<string, string> { ["columns"] = "default" }, Caller.ForEvaluation(), CurrentRecord: current);

    private static QuestionAnalysis AccountMention(string name = "Brightwave", bool referencesCurrent = false)
        => new QuestionAnalysis("account", new List<string>(), referencesCurrent ? new List<Mention>() : new List<Mention> { new Mention(name, "account") }, new List<string>(), false, false, referencesCurrent);

    /// <summary>
    /// Verifies that a summary request for one account returns the summary without the model planner.
    /// </summary>
    [Fact]
    public async Task ASummaryRequest_ForOneAccount_ReturnsTheSummary_WithoutTheModelPlanner()
    {
        var planner = new Planner();
        var executor = new Executor();

        var outcome = await Build(AccountMention(), planner, executor).RunAsync(Ask("Summarize Brightwave"));

        var summary = Assert.IsType<SummaryOutcome>(outcome);
        Assert.Equal(0, planner.Calls);
        Assert.Equal("account", summary.Entity);
        Assert.Equal("Brightwave Appliances Ltd", summary.RecordName);
        Assert.Equal(AccountId.ToString("N"), summary.RecordId);
        Assert.Contains(summary.Sections, s => s.Kind == "basic");
        Assert.NotEmpty(executor.Callers);
    }

    /// <summary>
    /// Verifies that without the summary word the question takes the normal path.
    /// </summary>
    [Fact]
    public async Task WithoutTheSummaryWord_TheQuestionTakesTheNormalPath()
    {
        var planner = new Planner();

        var outcome = await Build(AccountMention(), planner, new Executor()).RunAsync(Ask("Show Brightwave"));

        Assert.IsType<AnswerOutcome>(outcome);
        Assert.Equal(1, planner.Calls);
    }

    /// <summary>
    /// Verifies that the summary word with a target that is not an account or contact takes the normal path.
    /// </summary>
    [Fact]
    public async Task TheSummaryWordWithATargetThatIsNotAnAccountOrContact_TakesTheNormalPath()
    {
        var planner = new Planner();
        var analysis = new QuestionAnalysis("product", new List<string>(), new List<Mention> { new Mention("Chair", "product") }, new List<string> { "name" }, false);
        var searcher = new Searcher(new Dictionary<string, RecordMatch> { ["Chair"] = new RecordMatch(Guid.NewGuid(), "Chair") });
        var metadata = TestMetadata.Create();
        var executor = new Executor();
        var pipeline = new AskPipeline(metadata, new Analyzer(analysis), searcher, new ColumnPolicy(new Views()), planner, executor,
            summary: new SummaryBuilder(metadata, executor, new Views()));

        var outcome = await pipeline.RunAsync(Ask("Summarize Chair sales report"));

        Assert.IsNotType<SummaryOutcome>(outcome);
        Assert.Equal(1, planner.Calls);
    }

    /// <summary>
    /// Verifies that when the feature is off a summary word changes nothing.
    /// </summary>
    [Fact]
    public async Task WhenTheFeatureIsOff_ASummaryWordChangesNothing()
    {
        var planner = new Planner();

        var outcome = await Build(AccountMention(), planner, new Executor(), summaryOn: false).RunAsync(Ask("Summarize Brightwave"));

        Assert.IsNotType<SummaryOutcome>(outcome);
        Assert.Equal(1, planner.Calls);
    }

    /// <summary>
    /// Verifies that on a form this account is summarised and the open record is named.
    /// </summary>
    [Fact]
    public async Task OnAForm_ThisAccountIsSummarised_AndTheOpenRecordIsNamed()
    {
        var planner = new Planner();
        var current = new ResolvedRecord("this record", "account", AccountId, "Brightwave Appliances Ltd");

        var outcome = await Build(AccountMention(referencesCurrent: true), planner, new Executor()).RunAsync(Ask("bu firmayı özetle", current));

        var summary = Assert.IsType<SummaryOutcome>(outcome);
        Assert.Equal("Brightwave Appliances Ltd", summary.AboutRecord);
        Assert.Equal(0, planner.Calls);
    }

    private sealed class TwoSteps : IQuestionDecomposer
    {
        /// <summary>
        /// Fake decomposer that returns two steps.
        /// </summary>
        public Task<IReadOnlyList<QuestionStep>> DecomposeAsync(string question, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QuestionStep>>(new List<QuestionStep>
            {
                new QuestionStep("Summarize Brightwave", StepCondition.Always),
                new QuestionStep("Show Brightwave", StepCondition.IfPreviousNotEmpty),
            });
    }

    /// <summary>
    /// Verifies that a step inside a chain never becomes a summary and the chain does not crash.
    /// </summary>
    [Fact]
    public async Task AStepInsideAChain_NeverBecomesASummary_AndTheChainDoesNotCrash()
    {
        var planner = new Planner();
        var metadata = TestMetadata.Create();
        var executor = new Executor();
        var searcher = new Searcher(new Dictionary<string, RecordMatch> { ["Brightwave"] = new RecordMatch(AccountId, "Brightwave Appliances Ltd") });
        var pipeline = new AskPipeline(metadata, new Analyzer(AccountMention()), searcher, new ColumnPolicy(new Views()), planner, executor, new TwoSteps(),
            summary: new SummaryBuilder(metadata, executor, new Views()));

        var outcome = await pipeline.RunAsync(Ask("Brightwave'i özetle, sonra kişilerini getir"));

        Assert.IsNotType<SummaryOutcome>(outcome);
        Assert.False(outcome is FailureOutcome { Kind: "unexpected" }, "the chain must not crash");
    }

    /// <summary>
    /// Verifies that a summary history entry is never the context of a follow up.
    /// </summary>
    [Fact]
    public async Task ASummaryHistoryEntry_IsNeverTheContextOfAFollowUp()
    {
        var history = new CrmNlQuery.Infrastructure.Persistence.Memory.History.MemoryHistoryStore();
        var owner = Guid.NewGuid();
        var chat = await history.CreateConversationAsync(owner, "chat");
        var id = await history.AddAsync(owner, "Summarize X", "account", "<fetch />", null, chat, kind: "summary");
        var context = new CrmNlQuery.Application.History.Services.ConversationContext(history, () => TimeSpan.FromMinutes(30));

        Assert.Null(await context.LoadLastAsync(owner, chat));
        Assert.Null(await context.LoadAsync(owner, id, ignoreAge: true));
    }

    /// <summary>
    /// Verifies that the summary runs as the calling user.
    /// </summary>
    [Fact]
    public async Task TheSummaryRunsAsTheCaller()
    {
        var executor = new Executor();
        var caller = Caller.User(Guid.NewGuid());

        await Build(AccountMention(), new Planner(), executor).RunAsync(new AskRequest("Summarize Brightwave", new Dictionary<string, string>(), caller));

        Assert.All(executor.Callers, c => Assert.Equal(caller, c));
    }
}
