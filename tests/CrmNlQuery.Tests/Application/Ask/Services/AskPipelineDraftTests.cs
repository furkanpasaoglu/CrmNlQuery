using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Drafts.Services;
using CrmNlQuery.Application.Summary.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Services;

/// <summary>
/// Tests the email draft path of the ask pipeline: an email request for exactly one resolved record yields a draft without calling the planner.
/// </summary>
public class AskPipelineDraftTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Analyzer : IQuestionAnalyzer
    {
        private readonly QuestionAnalysis _analysis;
        public Analyzer(QuestionAnalysis analysis) { _analysis = analysis; }
        /// <summary>
        /// Provides a fake question analyzer for the draft pipeline tests.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default) => Task.FromResult(_analysis);
    }

    private sealed class Searcher : IRecordSearcher
    {
        /// <summary>
        /// Provides a fake record searcher for the draft pipeline tests.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RecordMatch>>(text == "Brightwave" ? new List<RecordMatch> { new RecordMatch(AccountId, "Brightwave Appliances Ltd") }
                : text == "Alpine" ? new List<RecordMatch> { new RecordMatch(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Alpine GmbH") } : new List<RecordMatch>());
    }

    private sealed class Views : IDefaultViewSource
    {
        /// <summary>
        /// Provides fake default view columns for the draft pipeline tests.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(new[] { "name" });
    }

    private sealed class Planner : IPlanGenerator
    {
        public int Calls;
        /// <summary>
        /// Provides a fake planner that records calls for the draft pipeline tests.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new PlanOutcome(true, "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>", new List<string>()));
        }
    }

    private sealed class Executor : IQueryExecutor
    {
        /// <summary>
        /// Provides a fake query executor for the draft pipeline tests.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
            => Task.FromResult(fetchXml.Contains("aggregate=\"true\"")
                ? new ResultTable(new[] { "n" }, new List<IReadOnlyList<string>> { new[] { "1" } })
                : new ResultTable(new[] { "name" }, new List<IReadOnlyList<string>> { new[] { "Brightwave" } }));
    }

    private static AskPipeline Build(Planner planner, FakeLlm llm, bool draftOn = true, bool summaryOn = true)
    {
        var metadata = TestMetadata.Create();
        var executor = new Executor();
        var summary = new SummaryBuilder(metadata, executor, new Views());
        var draft = new DraftService(new DraftValueReader(metadata, executor, summary), new DraftComposer(llm));
        var analysis = new QuestionAnalysis("account", new List<string>(), new List<Mention> { new Mention("Brightwave", "account") }, new List<string>(), false);
        return new AskPipeline(metadata, new Analyzer(analysis), new Searcher(), new ColumnPolicy(new Views()), planner, executor,
            summary: summary, summaryEnabled: () => summaryOn, draft: draft, draftEnabled: () => draftOn);
    }

    private static AskRequest Ask(string question) => new AskRequest(question, new Dictionary<string, string> { ["columns"] = "default" }, Caller.ForEvaluation());

    private const string Reply = """{"subject":"Hello {{customer_name}}","body":"Dear customer, best regards {{sender_name}}"}""";

    /// <summary>
    /// Verifies that an email request for one account returns a draft without calling the planner.
    /// </summary>
    [Fact]
    public async Task AnEmailRequest_ForOneAccount_ReturnsADraft_WithoutThePlanner()
    {
        var planner = new Planner();

        var outcome = await Build(planner, new FakeLlm(Reply)).RunAsync(Ask("write a follow-up email to Brightwave"));

        var draft = Assert.IsType<DraftOutcome>(outcome);
        Assert.Equal(0, planner.Calls);
        Assert.Equal("Hello Brightwave Appliances Ltd", draft.Subject);
        Assert.Equal(AccountId.ToString("N"), draft.RecordId);
    }

    /// <summary>
    /// Verifies that a question without an email word takes the normal path.
    /// </summary>
    [Fact]
    public async Task WithoutAnEmailWord_TheQuestionTakesTheNormalPath()
    {
        var planner = new Planner();
        var llm = new FakeLlm(Reply);

        var outcome = await Build(planner, llm).RunAsync(Ask("Show Brightwave"));

        Assert.IsType<AnswerOutcome>(outcome);
        Assert.Empty(llm.Calls);
    }

    /// <summary>
    /// Verifies that when the feature is off an email request changes nothing.
    /// </summary>
    [Fact]
    public async Task WhenTheFeatureIsOff_AnEmailRequestChangesNothing()
    {
        var planner = new Planner();
        var llm = new FakeLlm(Reply);

        var outcome = await Build(planner, llm, draftOn: false).RunAsync(Ask("write an email to Brightwave"));

        Assert.IsNotType<DraftOutcome>(outcome);
        Assert.Empty(llm.Calls);
        Assert.Equal(1, planner.Calls);
    }

    /// <summary>
    /// Verifies that a summary word wins over the email words.
    /// </summary>
    [Fact]
    public async Task ASummaryWordWins_OverTheEmailWords()
    {
        var planner = new Planner();
        var llm = new FakeLlm(Reply);

        var outcome = await Build(planner, llm).RunAsync(Ask("Summarize Brightwave and write an email"));

        Assert.IsType<SummaryOutcome>(outcome);
        Assert.Empty(llm.Calls);
    }

    /// <summary>
    /// Verifies that an email request naming no record is not treated as a draft.
    /// </summary>
    [Fact]
    public async Task AnEmailRequest_ThatNamesNoRecord_IsNotADraft()
    {
        var planner = new Planner();
        var metadata = TestMetadata.Create();
        var executor = new Executor();
        var summary = new SummaryBuilder(metadata, executor, new Views());
        var llm = new FakeLlm(Reply);
        var none = new QuestionAnalysis("account", new List<string>(), new List<Mention>(), new List<string> { "name" }, false);
        var pipeline = new AskPipeline(metadata, new Analyzer(none), new Searcher(), new ColumnPolicy(new Views()), planner, executor, summary: summary,
            draft: new DraftService(new DraftValueReader(metadata, executor, summary), new DraftComposer(llm)));

        var outcome = await pipeline.RunAsync(Ask("write an email"));

        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("draft_target", failure.Kind);
        Assert.Equal(UserMessages.DraftNeedsRecord, failure.Message);
        Assert.Empty(llm.Calls);
        Assert.Equal(0, planner.Calls);
    }

    /// <summary>
    /// Verifies that an email request for two records asks which one instead of running a data query.
    /// </summary>
    [Fact]
    public async Task AnEmailRequest_ForTwoRecords_AsksWhichOne_InsteadOfRunningADataQuery()
    {
        var planner = new Planner();
        var metadata = TestMetadata.Create();
        var executor = new Executor();
        var summary = new SummaryBuilder(metadata, executor, new Views());
        var llm = new FakeLlm(Reply);
        var analysis = new QuestionAnalysis("account", new List<string>(), new List<Mention> { new Mention("Brightwave", "account"), new Mention("Alpine", "account") }, new List<string>(), false);
        var pipeline = new AskPipeline(metadata, new Analyzer(analysis), new Searcher(), new ColumnPolicy(new Views()), planner, executor, summary: summary,
            draft: new DraftService(new DraftValueReader(metadata, executor, summary), new DraftComposer(llm)));

        var outcome = await pipeline.RunAsync(Ask("write an email to Brightwave and Alpine"));

        Assert.Equal("draft_target", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Empty(llm.Calls);
        Assert.Equal(0, planner.Calls);
    }

    /// <summary>
    /// Verifies that a draft is stored in the chat with only the template and can be reopened from the result service.
    /// </summary>
    [Fact]
    public async Task ADraftIsStoredInTheChat_WithOnlyTheTemplate_AndOpenedAgainFromTheResultService()
    {
        var history = new CrmNlQuery.Infrastructure.Persistence.Memory.History.MemoryHistoryStore();
        var metadata = TestMetadata.Create();
        var executor = new Executor();
        var summary = new SummaryBuilder(metadata, executor, new Views());
        var llm = new FakeLlm(Reply);
        var draftService = new DraftService(new DraftValueReader(metadata, executor, summary), new DraftComposer(llm));
        var results = new CrmNlQuery.Application.History.Services.ResultService(history, metadata, executor, null, summary, draftService);
        var chats = new CrmNlQuery.Application.History.Services.ChatService(history);
        var turns = new CrmNlQuery.Application.History.Services.ChatTurnService(chats, new CrmNlQuery.Application.History.Services.ConversationContext(history, () => TimeSpan.FromMinutes(30)), results);
        var owner = Guid.NewGuid();
        var analysis = new QuestionAnalysis("account", new List<string>(), new List<Mention> { new Mention("Brightwave", "account") }, new List<string>(), false);
        var pipeline = new AskPipeline(metadata, new Analyzer(analysis), new Searcher(), new ColumnPolicy(new Views()), new Planner(), executor, summary: summary, draft: draftService);

        var outcome = await pipeline.RunAsync(new AskRequest("write a follow-up email to Brightwave", new Dictionary<string, string>(), Caller.User(owner)));
        var turn = await turns.PrepareAsync(owner, null);
        var stored = await turns.CompleteDraftAsync(Assert.IsType<DraftOutcome>(outcome), owner, "write a follow-up email to Brightwave", turn);

        Assert.NotNull(stored.ResultId);
        Assert.NotNull(stored.ChatId);
        var entry = (await history.GetAsync(owner, stored.ResultId!))!;
        Assert.Equal("draft", entry.Kind);
        Assert.DoesNotContain("Brightwave Appliances", entry.Payload!);
        Assert.IsType<DraftOutcome>(await results.OpenAnyAsync(stored.ResultId!, owner));
    }

    /// <summary>
    /// Verifies that a draft request inside a chain never becomes a draft.
    /// </summary>
    [Fact]
    public async Task ADraftRequest_InsideAChain_NeverBecomesADraft()
    {
        var planner = new Planner();
        var metadata = TestMetadata.Create();
        var executor = new Executor();
        var summary = new SummaryBuilder(metadata, executor, new Views());
        var llm = new FakeLlm(Reply);
        var analysis = new QuestionAnalysis("account", new List<string>(), new List<Mention> { new Mention("Brightwave", "account") }, new List<string> { "name" }, false);
        var pipeline = new AskPipeline(metadata, new Analyzer(analysis), new Searcher(), new ColumnPolicy(new Views()), planner, executor, new TwoSteps(), summary: summary,
            draft: new DraftService(new DraftValueReader(metadata, executor, summary), new DraftComposer(llm)));

        var outcome = await pipeline.RunAsync(Ask("Brightwave'i bul, sonra e-posta yaz"));

        Assert.IsNotType<DraftOutcome>(outcome);
        Assert.Empty(llm.Calls);
    }

    private sealed class TwoSteps : IQuestionDecomposer
    {
        /// <summary>
        /// Provides a fake decomposer returning two steps for the draft pipeline tests.
        /// </summary>
        public Task<IReadOnlyList<QuestionStep>> DecomposeAsync(string question, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QuestionStep>>(new List<QuestionStep>
            {
                new QuestionStep("Show Brightwave", StepCondition.Always),
                new QuestionStep("write an email to Brightwave", StepCondition.IfPreviousNotEmpty),
            });
    }

    /// <summary>
    /// Verifies that a single named account still gets the draft when the analyzer picks no usable table.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("email")]
    public async Task WhenTheAnalyserPicksNoUsableTable_TheSingleNamedAccountStillGetsTheDraft(string primary)
    {
        var planner = new Planner();
        var metadata = TestMetadata.Create();
        var executor = new Executor();
        var summary = new SummaryBuilder(metadata, executor, new Views());
        var llm = new FakeLlm(Reply);
        var analysis = new QuestionAnalysis(primary, new List<string>(), new List<Mention> { new Mention("Brightwave", "account") }, new List<string>(), false);
        var pipeline = new AskPipeline(metadata, new Analyzer(analysis), new Searcher(), new ColumnPolicy(new Views()), planner, executor, summary: summary,
            draft: new DraftService(new DraftValueReader(metadata, executor, summary), new DraftComposer(llm)));

        var outcome = await pipeline.RunAsync(Ask("write a follow-up email to Brightwave"));

        Assert.IsType<DraftOutcome>(outcome);
        Assert.Equal(0, planner.Calls);
    }
}
