using System.Text.Json;
using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Ask;

/// <summary>
/// Tests the multi-step ask pipeline: conditional steps, step prefixes, clarifications, decomposer routing and chain behavior.
/// </summary>
public class MultiStepPipelineTests
{
    private const string Fetch = "<fetch top=\"500\"><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>";
    private const string Empty = """{"value":[]}""";

    private static string OneRow(string name) => "{\"value\":[{\"name\":\"" + name + "\"}]}";

    private sealed class FakeDecomposer : IQuestionDecomposer
    {
        private readonly IReadOnlyList<QuestionStep>? _steps;
        private readonly Exception? _throw;
        public int Calls { get; private set; }
        public FakeDecomposer(params QuestionStep[] steps) { _steps = steps; }
        public FakeDecomposer(Exception ex) { _throw = ex; }
        /// <summary>
        /// Fake decomposer that returns a preset list of question steps.
        /// </summary>
        public Task<IReadOnlyList<QuestionStep>> DecomposeAsync(string question, CancellationToken ct = default)
        {
            Calls++;
            if (_throw is not null) throw _throw;
            return Task.FromResult(_steps!);
        }
    }

    private sealed class QuestionKeyedAnalyzer : IQuestionAnalyzer
    {
        private readonly Dictionary<string, QuestionAnalysis> _byQuestion;
        private readonly QuestionAnalysis _default;
        public List<string> Seen { get; } = new List<string>();
        public QuestionKeyedAnalyzer(QuestionAnalysis fallback, Dictionary<string, QuestionAnalysis>? byQuestion = null)
        {
            _default = fallback;
            _byQuestion = byQuestion ?? new Dictionary<string, QuestionAnalysis>();
        }
        /// <summary>
        /// Fake analyzer that returns an analysis chosen by the question text.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default)
        {
            Seen.Add(question);
            return Task.FromResult(_byQuestion.TryGetValue(question, out var found) ? found : _default);
        }
    }

    private sealed class SequenceExecutor : IQueryExecutor
    {
        private readonly Queue<string> _replies;
        public int Calls { get; private set; }
        public List<Guid?> CallerIds { get; } = new List<Guid?>();
        public SequenceExecutor(params string[] replies) { _replies = new Queue<string>(replies); }
        /// <summary>
        /// Fake executor that returns preset result tables in sequence, one per executed step.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Calls++;
            CallerIds.Add(caller.UserIdOrNull());
            var json = _replies.Count > 0 ? _replies.Dequeue() : Empty;
            return Task.FromResult(ResultTableBuilder.Build(JsonDocument.Parse(json).RootElement.GetProperty("value").Clone(), fetchXml));
        }
    }

    private sealed class FakeSearcher : IRecordSearcher
    {
        private readonly Dictionary<string, List<RecordMatch>> _byText;
        public FakeSearcher(Dictionary<string, List<RecordMatch>> byText) { _byText = byText; }
        /// <summary>
        /// Fake record searcher that returns canned name matches for a lookup.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
        {
            IReadOnlyList<RecordMatch> list = _byText.TryGetValue(text, out var found) ? found : new List<RecordMatch>();
            return Task.FromResult(list);
        }
    }

    private sealed class FakeViews : IDefaultViewSource
    {
        /// <summary>
        /// Fake default-view source that returns canned default column names.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
        {
            IReadOnlyList<string> columns = new List<string> { "name" };
            return Task.FromResult(columns);
        }
    }

    private sealed class FixedPlanner : IPlanGenerator
    {
        /// <summary>
        /// Fake planner that always returns a fixed plan.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
            => Task.FromResult(new PlanOutcome(true, Fetch, new List<string>()));
    }

    private static QuestionAnalysis Product() =>
        new QuestionAnalysis("product", new List<string>(), new List<Mention>(), new List<string> { "name" }, false);

    private static QuestionAnalysis ProductOfCompany(string company) =>
        new QuestionAnalysis("product", new List<string>(), new List<Mention> { new Mention(company, "account") }, new List<string> { "name" }, false);

    private sealed class NeedsStepsForQuestionPlanner : IPlanGenerator
    {
        private readonly string _question;
        public NeedsStepsForQuestionPlanner(string question) { _question = question; }
        /// <summary>
        /// Fake planner that reports that the question needs several steps.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
            => Task.FromResult(request.Question == _question
                ? new PlanOutcome(false, null, new List<string> { "needs steps" }, NeedsSteps: true)
                : new PlanOutcome(true, Fetch, new List<string>()));
    }

    private static AskPipeline Build(IQuestionAnalyzer analyzer, IQueryExecutor executor, IQuestionDecomposer? decomposer,
        FakeSearcher? searcher = null, IPlanGenerator? planner = null)
    {
        return new AskPipeline(
            TestMetadata.Create(),
            analyzer,
            searcher ?? new FakeSearcher(new Dictionary<string, List<RecordMatch>>()),
            new ColumnPolicy(new FakeViews()),
            planner ?? new FixedPlanner(),
            executor,
            decomposer);
    }

    private static AskRequest Ask(string q, params (string Key, string Value)[] answers) =>
        new AskRequest(q, answers.ToDictionary(a => a.Key, a => a.Value), Caller.ForEvaluation());

    private static QuestionStep Always(string q) => new QuestionStep(q, StepCondition.Always);
    private static QuestionStep IfEmpty(string q) => new QuestionStep(q, StepCondition.IfPreviousEmpty);
    private static QuestionStep IfNotEmpty(string q) => new QuestionStep(q, StepCondition.IfPreviousNotEmpty);

    /// <summary>
    /// Verifies that a single-step plan runs with the original question and adds no step notes.
    /// </summary>
    [Fact]
    public async Task SingleStepPlan_UsesTheOriginalQuestion_AndNoNotes()
    {
        var analyzer = new QuestionKeyedAnalyzer(Product());
        var pipeline = Build(analyzer, new SequenceExecutor(OneRow("Chair")), new FakeDecomposer(Always("rephrased by model")));
        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("original yoksa question")));
        Assert.Equal(new[] { "original yoksa question" }, analyzer.Seen);
        Assert.Empty(answer.StepNotes);
    }

    /// <summary>
    /// Verifies that an if-previous-empty step runs when the first step returned no rows.
    /// </summary>
    [Fact]
    public async Task IfPreviousEmpty_FirstStepEmpty_RunsSecondStep()
    {
        var executor = new SequenceExecutor(Empty, OneRow("FromStep2"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), executor, new FakeDecomposer(Always("A"), IfEmpty("B")));
        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Equal("FromStep2", answer.Table.Rows[0][0]);
        Assert.Equal(2, executor.Calls);
        Assert.Contains(answer.StepNotes, n => n.Contains("Step 1") && n.Contains("no rows"));
    }

    /// <summary>
    /// Verifies that an if-previous-empty step is skipped when the first step returned rows.
    /// </summary>
    [Fact]
    public async Task IfPreviousEmpty_FirstStepHasRows_SkipsSecondStep()
    {
        var executor = new SequenceExecutor(OneRow("FromStep1"), OneRow("FromStep2"));
        var analyzer = new QuestionKeyedAnalyzer(Product());
        var pipeline = Build(analyzer, executor, new FakeDecomposer(Always("A"), IfEmpty("B")));
        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Equal("FromStep1", answer.Table.Rows[0][0]);
        Assert.Equal(1, executor.Calls);
        Assert.Equal(new[] { "A" }, analyzer.Seen);
        Assert.Contains(answer.StepNotes, n => n.Contains("Step 2") && n.Contains("skipped"));
    }

    /// <summary>
    /// Verifies that an if-previous-not-empty step is skipped when the first step returned no rows.
    /// </summary>
    [Fact]
    public async Task IfPreviousNotEmpty_FirstStepEmpty_SkipsSecondStep()
    {
        var executor = new SequenceExecutor(Empty, OneRow("FromStep2"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), executor, new FakeDecomposer(Always("A"), IfNotEmpty("B")));
        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Empty(answer.Table.Rows);
        Assert.Equal(1, executor.Calls);
    }

    /// <summary>
    /// Verifies that an if-previous-not-empty step runs when the first step returned rows.
    /// </summary>
    [Fact]
    public async Task IfPreviousNotEmpty_FirstStepHasRows_RunsSecondStep()
    {
        var executor = new SequenceExecutor(OneRow("FromStep1"), OneRow("FromStep2"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), executor, new FakeDecomposer(Always("A"), IfNotEmpty("B")));
        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Equal("FromStep2", answer.Table.Rows[0][0]);
        Assert.Equal(2, executor.Calls);
    }

    /// <summary>
    /// Verifies that when every step is empty the last executed empty table is returned.
    /// </summary>
    [Fact]
    public async Task AllStepsEmpty_ReturnsLastExecutedEmptyTable()
    {
        var executor = new SequenceExecutor(Empty, Empty);
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), executor, new FakeDecomposer(Always("A"), IfEmpty("B")));
        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Empty(answer.Table.Rows);
        Assert.Equal(new[] { "name" }, answer.Table.Columns);
        Assert.Equal(2, executor.Calls);
    }

    /// <summary>
    /// Verifies that in a three-step plan the condition looks at the last executed step.
    /// </summary>
    [Fact]
    public async Task ThreeSteps_ConditionLooksAtTheLastExecutedStep()
    {
        var executor = new SequenceExecutor(Empty, Empty, OneRow("FromStep3"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), executor, new FakeDecomposer(Always("A"), IfEmpty("B"), IfEmpty("C")));
        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Equal("FromStep3", answer.Table.Rows[0][0]);
        Assert.Equal(3, executor.Calls);
    }

    /// <summary>
    /// Verifies that a skipped middle step does not rescue the third step.
    /// </summary>
    [Fact]
    public async Task ThreeSteps_SkippedMiddleStep_DoesNotRescueTheThird()
    {
        var executor = new SequenceExecutor(OneRow("FromStep1"), OneRow("never"), OneRow("never"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), executor, new FakeDecomposer(Always("A"), IfEmpty("B"), IfEmpty("C")));
        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Equal("FromStep1", answer.Table.Rows[0][0]);
        Assert.Equal(1, executor.Calls);
    }

    /// <summary>
    /// Verifies that each step runs with its own question text.
    /// </summary>
    [Fact]
    public async Task StepsRunWithTheirOwnQuestions()
    {
        var analyzer = new QuestionKeyedAnalyzer(Product());
        var pipeline = Build(analyzer, new SequenceExecutor(Empty, OneRow("x")), new FakeDecomposer(Always("contracts of Denema"), IfEmpty("contracts of Ornekova")));
        await pipeline.RunAsync(Ask("whole yoksa question"));
        Assert.Equal(new[] { "contracts of Denema", "contracts of Ornekova" }, analyzer.Seen);
    }

    /// <summary>
    /// Verifies that a company not found in the second step stops with the step number and no guessing.
    /// </summary>
    [Fact]
    public async Task CompanyNotFoundInSecondStep_StopsWithStepNumber_NoGuessing()
    {
        var analyzer = new QuestionKeyedAnalyzer(Product(), new Dictionary<string, QuestionAnalysis> { ["B"] = ProductOfCompany("Zorlu Holding") });
        var executor = new SequenceExecutor(Empty, OneRow("never"));
        var pipeline = Build(analyzer, executor, new FakeDecomposer(Always("A"), IfEmpty("B")));
        var failure = Assert.IsType<FailureOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Contains("Step 2", failure.Message);
        Assert.Contains("Zorlu Holding", failure.Message);
        Assert.Equal(1, executor.Calls);
    }

    /// <summary>
    /// Verifies that a company not found in the first step fails and never falls through to the else step.
    /// </summary>
    [Fact]
    public async Task CompanyNotFoundInFirstStep_FailsAndNeverFallsThroughToTheElseStep()
    {
        var analyzer = new QuestionKeyedAnalyzer(Product(), new Dictionary<string, QuestionAnalysis> { ["A"] = ProductOfCompany("Zorlu Holding") });
        var executor = new SequenceExecutor(OneRow("never"));
        var pipeline = Build(analyzer, executor, new FakeDecomposer(Always("A"), IfEmpty("B")));
        var failure = Assert.IsType<FailureOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Contains("Step 1", failure.Message);
        Assert.Equal(0, executor.Calls);
    }

    private static FakeSearcher TwoDenemas() => new FakeSearcher(new Dictionary<string, List<RecordMatch>>
    {
        ["Denema"] = new List<RecordMatch> { new RecordMatch(Guid.NewGuid(), "Denema Sigorta A.Ş."), new RecordMatch(Guid.NewGuid(), "Denema Sağlık Sigorta") }
    });

    /// <summary>
    /// Verifies that a clarification asked in the second step is prefixed with the step number.
    /// </summary>
    [Fact]
    public async Task ClarificationInSecondStep_IsPrefixedWithTheStepNumber()
    {
        var analyzer = new QuestionKeyedAnalyzer(Product(), new Dictionary<string, QuestionAnalysis> { ["B"] = ProductOfCompany("Denema") });
        var pipeline = Build(analyzer, new SequenceExecutor(Empty), new FakeDecomposer(Always("A"), IfEmpty("B")), TwoDenemas());
        var question = Assert.IsType<QuestionOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Equal("s2:resolve:Denema", question.AnswerKey);
    }

    /// <summary>
    /// Verifies that a clarification answer carrying a step prefix continues the flow.
    /// </summary>
    [Fact]
    public async Task ClarificationAnswer_WithStepPrefix_ContinuesTheFlow()
    {
        var analyzer = new QuestionKeyedAnalyzer(Product(), new Dictionary<string, QuestionAnalysis> { ["B"] = ProductOfCompany("Denema") });
        var executor = new SequenceExecutor(Empty, OneRow("FromStep2"));
        var pipeline = Build(analyzer, executor, new FakeDecomposer(Always("A"), IfEmpty("B")), TwoDenemas());
        var answer = Assert.IsType<AnswerOutcome>(
            await pipeline.RunAsync(Ask("a yoksa b", ("s2:resolve:Denema", $"{Guid.NewGuid()}::Denema Sigorta A.Ş."))));
        Assert.Equal("FromStep2", answer.Table.Rows[0][0]);
    }

    /// <summary>
    /// Verifies that a clarification answer without a step prefix is not applied.
    /// </summary>
    [Fact]
    public async Task ClarificationAnswer_WithoutStepPrefix_IsNotApplied()
    {
        var analyzer = new QuestionKeyedAnalyzer(Product(), new Dictionary<string, QuestionAnalysis> { ["B"] = ProductOfCompany("Denema") });
        var pipeline = Build(analyzer, new SequenceExecutor(Empty), new FakeDecomposer(Always("A"), IfEmpty("B")), TwoDenemas());
        var question = Assert.IsType<QuestionOutcome>(
            await pipeline.RunAsync(Ask("a yoksa b", ("resolve:Denema", $"{Guid.NewGuid()}::Denema Sigorta A.Ş."))));
        Assert.Equal("s2:resolve:Denema", question.AnswerKey);
    }

    /// <summary>
    /// Verifies that a column question in the first step is prefixed and can be answered.
    /// </summary>
    [Fact]
    public async Task ColumnQuestionInFirstStep_IsPrefixed_AndAnswerable()
    {
        var noColumns = new QuestionAnalysis("product", new List<string>(), new List<Mention>(), new List<string>(), false);
        var pipeline = Build(new QuestionKeyedAnalyzer(noColumns), new SequenceExecutor(OneRow("Chair")), new FakeDecomposer(Always("A"), IfEmpty("B")));

        var question = Assert.IsType<QuestionOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Equal("s1:columns", question.AnswerKey);

        var answer = await pipeline.RunAsync(Ask("a yoksa b", ("s1:columns", "default")));
        Assert.IsType<AnswerOutcome>(answer);
    }

    /// <summary>
    /// Verifies that without a decomposer the pipeline behaves as a single-step pipeline.
    /// </summary>
    [Fact]
    public async Task NoDecomposer_BehavesAsBefore()
    {
        var analyzer = new QuestionKeyedAnalyzer(Product());
        var pipeline = Build(analyzer, new SequenceExecutor(OneRow("Chair")), null);
        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
        Assert.Empty(answer.StepNotes);
        Assert.Equal(new[] { "a yoksa b" }, analyzer.Seen);
    }

    /// <summary>
    /// Verifies that a decomposer output problem becomes a failure with a user message.
    /// </summary>
    [Fact]
    public async Task DecomposerOutputProblem_IsFailureWithMessage()
    {
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), new SequenceExecutor(), new FakeDecomposer(new LlmOutputException("bad")));
        Assert.IsType<FailureOutcome>(await pipeline.RunAsync(Ask("a yoksa b")));
    }

    /// <summary>
    /// Verifies that every step runs as the same calling user.
    /// </summary>
    [Fact]
    public async Task EveryStep_RunsAsTheSameCaller()
    {
        var caller = Guid.NewGuid();
        var executor = new SequenceExecutor(Empty, Empty, OneRow("x"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), executor, new FakeDecomposer(Always("A"), IfEmpty("B"), IfEmpty("C")));

        await pipeline.RunAsync(new AskRequest("a yoksa b", new Dictionary<string, string>(), Caller.User(caller)));

        Assert.Equal(new Guid?[] { caller, caller, caller }, executor.CallerIds);
    }

    /// <summary>
    /// Verifies that a question without conditional wording never calls the decomposer.
    /// </summary>
    [Fact]
    public async Task QuestionWithoutConditionalWording_NeverCallsTheDecomposer()
    {
        var decomposer = new FakeDecomposer(Always("A"), IfEmpty("B"));
        var analyzer = new QuestionKeyedAnalyzer(Product());
        var executor = new SequenceExecutor(OneRow("Chair"));
        var pipeline = Build(analyzer, executor, decomposer);

        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("all the products")));
        Assert.Equal(0, decomposer.Calls);
        Assert.Equal(new[] { "all the products" }, analyzer.Seen);
        Assert.Equal(1, executor.Calls);
        Assert.Empty(answer.StepNotes);
    }

    /// <summary>
    /// Verifies that a question with conditional wording calls the decomposer exactly once.
    /// </summary>
    [Fact]
    public async Task QuestionWithConditionalWording_CallsTheDecomposerOnce()
    {
        var decomposer = new FakeDecomposer(Always("A"), IfEmpty("B"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), new SequenceExecutor(Empty, OneRow("x")), decomposer);
        await pipeline.RunAsync(Ask("Denema contracts, otherwise Ornekova contracts"));
        Assert.Equal(1, decomposer.Calls);
    }

    /// <summary>
    /// Verifies that when the planner says it needs steps a question without trigger words falls back to the decomposer.
    /// </summary>
    [Fact]
    public async Task WhenThePlannerSaysItNeedsSteps_AQuestionWithoutTriggerWords_FallsBackToTheDecomposer()
    {
        var decomposer = new FakeDecomposer(Always("A"), Always("B"));
        var executor = new SequenceExecutor(OneRow("x"), OneRow("y"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), executor, decomposer, planner: new NeedsStepsForQuestionPlanner("bunu getir"));

        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("bunu getir")));

        Assert.Equal(1, decomposer.Calls);
        Assert.Equal(2, executor.Calls);
        Assert.Equal("y", answer.Table.Rows[0][0]);
    }

    /// <summary>
    /// Verifies that when the decomposer still returns one step, the user gets a clear message instead of a guess.
    /// </summary>
    [Fact]
    public async Task WhenTheDecomposerStillSaysOneStep_TheUserGetsAClearMessage_NotAGuess()
    {
        var decomposer = new FakeDecomposer(Always("only one"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), new SequenceExecutor(), decomposer, planner: new NeedsStepsForQuestionPlanner("bunu getir"));

        var failure = Assert.IsType<FailureOutcome>(await pipeline.RunAsync(Ask("bunu getir")));

        Assert.Equal(AskPipeline.NeedsStepsKind, failure.Kind);
        Assert.Equal(UserMessages.NeedsSteps, failure.Message);
    }

    /// <summary>
    /// Verifies that the decomposer is called at most twice for one question.
    /// </summary>
    [Fact]
    public async Task TheDecomposerIsCalledAtMostTwice_ForOneQuestion()
    {
        var decomposer = new FakeDecomposer(Always("only one"));
        const string question = "en çok olanı getir yoksa hiçbirini";
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), new SequenceExecutor(), decomposer, planner: new NeedsStepsForQuestionPlanner(question));

        var failure = Assert.IsType<FailureOutcome>(await pipeline.RunAsync(Ask(question)));

        Assert.Equal(2, decomposer.Calls);
        Assert.Equal(AskPipeline.NeedsStepsKind, failure.Kind);
    }

    /// <summary>
    /// Verifies that a ranking-only question with no related entity falls back to the decomposer once when the planner flags it.
    /// </summary>
    [Fact]
    public async Task ARankingOnlyQuestion_WithNoRelatedEntity_WhenThePlannerFlagsIt_FallsBackToTheDecomposerOnce()
    {
        var decomposer = new FakeDecomposer(Always("only one"));
        var analyzer = new QuestionKeyedAnalyzer(Product());
        var pipeline = Build(analyzer, new SequenceExecutor(), decomposer, planner: new NeedsStepsForQuestionPlanner("en çok olanı getir"));

        var failure = Assert.IsType<FailureOutcome>(await pipeline.RunAsync(Ask("en çok olanı getir")));

        Assert.Equal(1, decomposer.Calls);
        Assert.Equal(AskPipeline.NeedsStepsKind, failure.Kind);
    }

    /// <summary>
    /// Verifies that without a decomposer the needs-steps flag produces the clear message.
    /// </summary>
    [Fact]
    public async Task WithoutADecomposer_TheFlagGivesTheClearMessage()
    {
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), new SequenceExecutor(), null, planner: new NeedsStepsForQuestionPlanner("bunu getir"));

        var failure = Assert.IsType<FailureOutcome>(await pipeline.RunAsync(Ask("bunu getir")));

        Assert.Equal(UserMessages.NeedsSteps, failure.Message);
    }

    /// <summary>
    /// Verifies that a plain question never touches the decomposer when the planner does not flag it.
    /// </summary>
    [Fact]
    public async Task APlainQuestion_NeverTouchesTheDecomposer_WhenThePlannerDoesNotFlagIt()
    {
        var decomposer = new FakeDecomposer(Always("A"), Always("B"));
        var pipeline = Build(new QuestionKeyedAnalyzer(Product()), new SequenceExecutor(OneRow("x")), decomposer);

        await pipeline.RunAsync(Ask("all the products"));

        Assert.Equal(0, decomposer.Calls);
    }

    private static QuestionAnalysis ProductWithRelated(params string[] related) =>
        new QuestionAnalysis("product", related.ToList(), new List<Mention>(), new List<string> { "name" }, false);

    /// <summary>
    /// Verifies that a ranking question with no related entity skips the decomposer and is analysed only once.
    /// </summary>
    [Fact]
    public async Task ARankingQuestion_WithNoRelatedEntity_SkipsTheDecomposer_AndIsAnalysedOnlyOnce()
    {
        var decomposer = new FakeDecomposer(Always("A"), Always("B"));
        var analyzer = new QuestionKeyedAnalyzer(Product());
        var executor = new SequenceExecutor(OneRow("Chair"));
        var pipeline = Build(analyzer, executor, decomposer);

        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("en çok çalışanı olan 5 firmayı getir")));

        Assert.Equal(0, decomposer.Calls);
        Assert.Equal(new[] { "en çok çalışanı olan 5 firmayı getir" }, analyzer.Seen);
        Assert.Equal(1, executor.Calls);
        Assert.Empty(answer.StepNotes);
    }

    /// <summary>
    /// Verifies that a ranking question with a related entity runs the decomposer and analyses the steps separately.
    /// </summary>
    [Fact]
    public async Task ARankingQuestion_WithARelatedEntity_RunsTheDecomposer_AndTheStepsAreAnalysedSeparately()
    {
        var decomposer = new FakeDecomposer(Always("step A"), Always("step B"));
        var analyzer = new QuestionKeyedAnalyzer(Product(), new Dictionary<string, QuestionAnalysis> { ["en çok olanın kişileri"] = ProductWithRelated("account") });
        var executor = new SequenceExecutor(OneRow("x"), OneRow("y"));
        var pipeline = Build(analyzer, executor, decomposer);

        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("en çok olanın kişileri")));

        Assert.Equal(1, decomposer.Calls);
        Assert.Equal(2, executor.Calls);
        Assert.Equal(new[] { "en çok olanın kişileri", "step A", "step B" }, analyzer.Seen);
        Assert.Equal("y", answer.Table.Rows[0][0]);
    }

    /// <summary>
    /// Verifies that when the decomposer says one step, the router's analysis is reused without a second analysis.
    /// </summary>
    [Fact]
    public async Task WhenTheDecomposerSaysOneStep_TheRoutersAnalysisIsReused_NoSecondAnalysis()
    {
        var decomposer = new FakeDecomposer(Always("rephrased"));
        var analyzer = new QuestionKeyedAnalyzer(Product(), new Dictionary<string, QuestionAnalysis> { ["en çok olanın kişileri"] = ProductWithRelated("account") });
        var executor = new SequenceExecutor(OneRow("x"));
        var pipeline = Build(analyzer, executor, decomposer);

        var answer = Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("en çok olanın kişileri")));

        Assert.Equal(1, decomposer.Calls);
        Assert.Equal(new[] { "en çok olanın kişileri" }, analyzer.Seen);
        Assert.Equal(1, executor.Calls);
        Assert.Empty(answer.StepNotes);
    }

    /// <summary>
    /// Verifies that conditional and sequential questions go straight to the decomposer without an analysis first.
    /// </summary>
    [Fact]
    public async Task ConditionalAndSequentialQuestions_GoStraightToTheDecomposer_WithoutAnAnalysisFirst()
    {
        var decomposer = new FakeDecomposer(Always("step A"), Always("step B"));
        var analyzer = new QuestionKeyedAnalyzer(Product());

        await Build(analyzer, new SequenceExecutor(OneRow("x"), OneRow("y")), decomposer).RunAsync(Ask("a yoksa b"));
        await Build(analyzer, new SequenceExecutor(OneRow("x"), OneRow("y")), decomposer).RunAsync(Ask("a getir, sonra b getir"));

        Assert.Equal(2, decomposer.Calls);
        Assert.DoesNotContain("a yoksa b", analyzer.Seen);
        Assert.DoesNotContain("a getir, sonra b getir", analyzer.Seen);
    }

    /// <summary>
    /// Verifies that a ranking question still runs as a single step when no decomposer is configured.
    /// </summary>
    [Fact]
    public async Task ARankingQuestion_WithNoDecomposerConfigured_StillRunsAsASingleStep()
    {
        var analyzer = new QuestionKeyedAnalyzer(Product());
        var pipeline = Build(analyzer, new SequenceExecutor(OneRow("Chair")), null);

        Assert.IsType<AnswerOutcome>(await pipeline.RunAsync(Ask("en çok çalışanı olan firma")));
        Assert.Equal(new[] { "en çok çalışanı olan firma" }, analyzer.Seen);
    }
}
