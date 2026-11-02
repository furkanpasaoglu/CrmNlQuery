using System.Text.Json;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Steps;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.External.Crm.Records;

namespace CrmNlQuery.Tests.Application.Ask;

/// <summary>
/// Tests the dependent multi-step chain in the pipeline, where earlier step ids fill later steps without the model seeing them.
/// </summary>
public class DependentChainPipelineTests
{
    private const string G1 = "11111111-1111-1111-1111-111111111111";
    private const string G2 = "22222222-2222-2222-2222-222222222222";
    private const string G3 = "33333333-3333-3333-3333-333333333333";
    private const string G4 = "44444444-4444-4444-4444-444444444444";

    private const string Step1Question = "the account with the most open opportunities";
    private const string Step2Question = "the contacts of those accounts";

    private const string RankOpportunities =
        "<fetch aggregate=\"true\" top=\"1\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />" +
        "<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /><order alias=\"n\" descending=\"true\" /></entity></fetch>";

    private const string ContactsOfStep1 =
        "<fetch top=\"500\"><entity name=\"contact\"><attribute name=\"fullname\" /><filter><condition attribute=\"parentcustomerid\" operator=\"in\"><value>{{step1}}</value></condition></filter></entity></fetch>";

    private static AttributeInfo A(string name, string type) => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static RelationshipInfo Rel(string entity, string attribute, string target) => new RelationshipInfo(entity + "_" + attribute + "_" + target, entity, attribute, target, target + "id");

    private static FakeMetadataSource Metadata()
    {
        var account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
            new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name", "String"), A("numberofemployees", "Integer") }, new List<RelationshipInfo>());
        var contact = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
            new List<AttributeInfo> { A("contactid", "Uniqueidentifier"), A("fullname", "String"), A("parentcustomerid", "Customer") },
            new List<RelationshipInfo> { Rel("contact", "parentcustomerid", "account"), Rel("contact", "parentcustomerid", "contact") });
        var opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
            new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name", "String"), A("customerid", "Customer") },
            new List<RelationshipInfo> { Rel("opportunity", "customerid", "account"), Rel("opportunity", "customerid", "contact") });
        return new FakeMetadataSource(account, contact, opportunity);
    }

    private sealed class FakeDecomposer : IQuestionDecomposer
    {
        private readonly IReadOnlyList<QuestionStep> _steps;
        public FakeDecomposer(params QuestionStep[] steps) { _steps = steps; }
        /// <summary>
        /// Returns scripted decomposition steps for the test.
        /// </summary>
        public Task<IReadOnlyList<QuestionStep>> DecomposeAsync(string question, CancellationToken ct = default) => Task.FromResult(_steps);
    }

    private sealed class KeyedAnalyzer : IQuestionAnalyzer
    {
        private readonly Dictionary<string, QuestionAnalysis> _byQuestion;
        public KeyedAnalyzer(Dictionary<string, QuestionAnalysis> byQuestion) { _byQuestion = byQuestion; }
        /// <summary>
        /// Returns a scripted question analysis for the test.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default) => Task.FromResult(_byQuestion[question]);
    }

    private sealed class ScriptedPlanner : IPlanGenerator
    {
        private readonly Dictionary<string, string> _fetchByQuestion;
        public List<PlanRequest> Requests { get; } = new List<PlanRequest>();
        public ScriptedPlanner(Dictionary<string, string> fetchByQuestion) { _fetchByQuestion = fetchByQuestion; }
        /// <summary>
        /// Returns a scripted plan for the test.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new PlanOutcome(true, _fetchByQuestion[request.Question], new List<string>()));
        }
    }

    private sealed class ScriptedExecutor : IQueryExecutor
    {
        private readonly Queue<string> _replies;
        public List<string> Fetches { get; } = new List<string>();
        public List<Guid?> CallerIds { get; } = new List<Guid?>();
        public ScriptedExecutor(params string[] replies) { _replies = new Queue<string>(replies); }
        /// <summary>
        /// Fake query executor that returns canned results for the test instead of calling CRM.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Fetches.Add(fetchXml);
            CallerIds.Add(caller.UserIdOrNull());
            var json = _replies.Count > 0 ? _replies.Dequeue() : "{\"value\":[]}";
            return Task.FromResult(ResultTableBuilder.Build(JsonDocument.Parse(json).RootElement.GetProperty("value").Clone(), fetchXml));
        }
    }

    private sealed class FakeSearcher : IRecordSearcher
    {
        /// <summary>
        /// Returns canned record matches for a name search in the test.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RecordMatch>>(new List<RecordMatch>());
    }

    private sealed class FakeViews : IDefaultViewSource
    {
        /// <summary>
        /// Returns canned default view columns for the test.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string> { "name" });
    }

    private static QuestionAnalysis RankAnalysis() =>
        new QuestionAnalysis("opportunity", new List<string>(), new List<Mention>(), new List<string>(), true);

    private static QuestionAnalysis ContactsAnalysis() =>
        new QuestionAnalysis("contact", new List<string>(), new List<Mention>(), new List<string> { "fullname" }, false);

    private sealed record Run(ScriptedPlanner Planner, ScriptedExecutor Executor, AskOutcome Outcome);

    private static async Task<Run> Execute(string rank, string[] replies, Guid? caller = null, QuestionAnalysis? step1 = null, bool wired = true)
    {
        var metadata = Metadata();
        var planner = new ScriptedPlanner(new Dictionary<string, string> { [Step1Question] = rank, [Step2Question] = ContactsOfStep1 });
        var executor = new ScriptedExecutor(replies);
        var analyzer = new KeyedAnalyzer(new Dictionary<string, QuestionAnalysis>
        {
            [Step1Question] = step1 ?? RankAnalysis(),
            [Step2Question] = ContactsAnalysis(),
            ["the contacts of the account with the most open opportunities"] =
                new QuestionAnalysis("contact", new List<string> { "account" }, new List<Mention>(), new List<string> { "fullname" }, false),
        });
        var pipeline = new AskPipeline(metadata, analyzer, new FakeSearcher(), new ColumnPolicy(new FakeViews()), planner, executor,
            new FakeDecomposer(new QuestionStep(Step1Question, StepCondition.Always), new QuestionStep(Step2Question, StepCondition.Always, 1)),
            stepFiller: wired ? new StepInputFiller(new PlanValidator(metadata)) : null,
            stepIdColumn: wired ? new StepIdColumn(metadata) : null);

        var outcome = await pipeline.RunAsync(new AskRequest("the contacts of the account with the most open opportunities", new Dictionary<string, string>(), caller is null ? Caller.ForEvaluation() : Caller.User(caller.Value)));
        return new Run(planner, executor, outcome);
    }

    private static string Rows(params (int N, string Id)[] rows) =>
        "{\"value\":[" + string.Join(",", rows.Select(r => $"{{\"n\":{r.N},\"_rowid\":\"{r.Id}\"}}")) + "]}";

    private const string TwoContacts = "{\"value\":[{\"fullname\":\"Freya\"},{\"fullname\":\"Grace\"}]}";

    /// <summary>
    /// Verifies that the IDs of step one are filled into step two and only the winner is used.
    /// </summary>
    [Fact]
    public async Task TheIdsOfStepOne_AreFilledIntoStepTwo_AndOnlyTheWinnerIsUsed()
    {
        var run = await Execute(RankOpportunities, new[] { Rows((3, G1), (1, G2)), TwoContacts });

        var answer = Assert.IsType<AnswerOutcome>(run.Outcome);
        Assert.Equal("contact", answer.Entity);
        Assert.Equal(new[] { "Freya", "Grace" }, answer.Table.Rows.Select(r => r[0]).ToArray());
        Assert.Equal(4, run.Executor.Fetches.Count);
        var second = run.Executor.Fetches[1];
        Assert.Contains(G1, second);
        Assert.DoesNotContain(G2, second);
        Assert.DoesNotContain("{{step1}}", second);
        Assert.Equal(second, answer.FetchXml);
    }

    /// <summary>
    /// Verifies that every step runs on behalf of the caller.
    /// </summary>
    [Fact]
    public async Task EveryStep_RunsOnBehalfOfTheCaller()
    {
        var caller = Guid.NewGuid();

        var run = await Execute(RankOpportunities, new[] { Rows((3, G1)), TwoContacts }, caller);

        Assert.Equal(new Guid?[] { caller, caller, caller, caller }, run.Executor.CallerIds.ToArray());
    }

    /// <summary>
    /// Verifies that the model never sees the IDs only the step info.
    /// </summary>
    [Fact]
    public async Task TheModelNeverSeesTheIds_OnlyTheStepInfo()
    {
        var run = await Execute(RankOpportunities, new[] { Rows((3, G1)), TwoContacts });

        Assert.IsType<AnswerOutcome>(run.Outcome);
        var first = run.Planner.Requests[0].Step!;
        var second = run.Planner.Requests[1].Step!;
        Assert.True(first.ProducesIds);
        Assert.Null(first.UsesStep);
        Assert.Equal(1, second.UsesStep);
        Assert.Contains("account", second.IdEntities);
        Assert.Contains("contact", second.IdEntities);
        Assert.DoesNotContain(G1, string.Join(" ", run.Planner.Requests.Select(r => r.Question + string.Join(" ", r.Columns))));
    }

    /// <summary>
    /// Verifies that the answer carries an ID free summary of the earlier step and the chain flag.
    /// </summary>
    [Fact]
    public async Task TheAnswerCarriesAnIdFreeSummaryOfTheEarlierStep_AndTheChainFlag()
    {
        var run = await Execute(RankOpportunities, new[] { Rows((3, G1), (1, G2)), TwoContacts });

        var answer = Assert.IsType<AnswerOutcome>(run.Outcome);
        var summary = Assert.Single(answer.StepSummaries);
        Assert.Equal(1, summary.Number);
        Assert.Equal(Step1Question, summary.Question);
        Assert.Equal(1, summary.Rows);
        Assert.Equal(new[] { "n" }, summary.Columns);
        Assert.Equal("3", summary.Preview[0][0]);
        Assert.DoesNotContain(G1, JsonSerializer.Serialize(summary));
        Assert.True(answer.FromChain);
        Assert.Empty(answer.NarrowHints);
    }

    /// <summary>
    /// Verifies that a tie uses every tied record and says so.
    /// </summary>
    [Fact]
    public async Task ATie_UsesEveryTiedRecord_AndSaysSo()
    {
        var fewest = RankOpportunities.Replace("descending=\"true\"", "descending=\"false\"");

        var run = await Execute(fewest, new[] { Rows((1, G1), (1, G2), (1, G3), (3, G4)), TwoContacts });

        var answer = Assert.IsType<AnswerOutcome>(run.Outcome);
        var second = run.Executor.Fetches[1];
        Assert.Contains(G1, second);
        Assert.Contains(G2, second);
        Assert.Contains(G3, second);
        Assert.DoesNotContain(G4, second);
        Assert.Contains(answer.StepNotes, n => n.Contains("Tie") && n.Contains("3"));
        Assert.Contains("top=\"101\"", run.Executor.Fetches[0]);
    }

    /// <summary>
    /// Verifies that an empty earlier step stops the chain with a note and never plans the next step.
    /// </summary>
    [Fact]
    public async Task AnEmptyEarlierStep_StopsTheChain_WithANote_AndNeverPlansTheNextStep()
    {
        var run = await Execute(RankOpportunities, new[] { "{\"value\":[]}" });

        var answer = Assert.IsType<AnswerOutcome>(run.Outcome);
        Assert.Empty(answer.Table.Rows);
        Assert.Single(run.Executor.Fetches);
        Assert.Single(run.Planner.Requests);
        Assert.Contains(answer.StepNotes, n => n.Contains("not run") && n.Contains("1"));
    }

    /// <summary>
    /// Verifies that more than the limit of IDs stops the chain and asks the user to narrow.
    /// </summary>
    [Fact]
    public async Task MoreThanTheLimitOfIds_StopsTheChain_AndAsksTheUserToNarrow()
    {
        var many = Enumerable.Range(1, StepInputFiller.MaxIds + 1).Select(i => (1, Guid.Parse($"{i:x8}-0000-0000-0000-000000000000").ToString())).ToArray();
        var noTop = RankOpportunities.Replace(" top=\"1\"", "");

        var run = await Execute(noTop, new[] { Rows(many) });

        var failure = Assert.IsType<FailureOutcome>(run.Outcome);
        Assert.Equal("too_many_ids", failure.Kind);
        Assert.Contains("100", failure.Message);
        Assert.Contains((StepInputFiller.MaxIds + 1).ToString(), failure.Message);
        Assert.Single(run.Executor.Fetches);
    }

    /// <summary>
    /// Verifies that exactly the limit of IDs still runs.
    /// </summary>
    [Fact]
    public async Task ExactlyTheLimitOfIds_StillRuns()
    {
        var many = Enumerable.Range(1, StepInputFiller.MaxIds).Select(i => (1, Guid.Parse($"{i:x8}-0000-0000-0000-000000000000").ToString())).ToArray();
        var noTop = RankOpportunities.Replace(" top=\"1\"", "");

        var run = await Execute(noTop, new[] { Rows(many), TwoContacts });

        Assert.IsType<AnswerOutcome>(run.Outcome);
        Assert.Equal(4, run.Executor.Fetches.Count);
    }

    /// <summary>
    /// Verifies that a plain ranking step gets the ID column no column question and the ordered attribute.
    /// </summary>
    [Fact]
    public async Task APlainRankingStep_GetsTheIdColumn_NoColumnQuestion_AndTheOrderedAttribute()
    {
        var plain = "<fetch top=\"1\"><entity name=\"account\"><attribute name=\"name\" /><order attribute=\"numberofemployees\" descending=\"true\" /></entity></fetch>";
        var analysis = new QuestionAnalysis("account", new List<string>(), new List<Mention>(), new List<string>(), false);
        var reply1 = "{\"value\":[{\"name\":\"Nordbeispiel\",\"numberofemployees\":3626,\"_rowid\":\"" + G1 + "\"},{\"name\":\"Ironclad\",\"numberofemployees\":3318,\"_rowid\":\"" + G2 + "\"}]}";

        var run = await Execute(plain, new[] { reply1, TwoContacts }, step1: analysis);

        Assert.IsType<AnswerOutcome>(run.Outcome);
        var first = run.Executor.Fetches[0];
        Assert.Contains("alias=\"_rowid\"", first);
        Assert.Contains("numberofemployees", first);
        Assert.Contains(G1, run.Executor.Fetches[1]);
        Assert.DoesNotContain(G2, run.Executor.Fetches[1]);
    }

    /// <summary>
    /// Verifies that without the step services a dependent step runs as before no IDs are carried.
    /// </summary>
    [Fact]
    public async Task WithoutTheStepServices_ADependentStepRunsAsBefore_NoIdsAreCarried()
    {
        var run = await Execute(RankOpportunities, new[] { Rows((3, G1)), TwoContacts }, wired: false);

        Assert.IsType<AnswerOutcome>(run.Outcome);
        Assert.Equal(2, run.Executor.Fetches.Count);
        Assert.Null(run.Planner.Requests[1].Step);
    }

    /// <summary>
    /// Verifies that the summary carries the names of the records step one picked resolved on behalf of the caller.
    /// </summary>
    [Fact]
    public async Task TheSummary_CarriesTheNamesOfTheRecordsStepOnePicked_ResolvedOnBehalfOfTheCaller()
    {
        var caller = Guid.NewGuid();
        var names = "{\"value\":[{\"name\":\"Brightwave Appliances Ltd\",\"_rowid\":\"" + G1 + "\"}]}";

        var run = await Execute(RankOpportunities, new[] { Rows((3, G1), (1, G2)), TwoContacts, names }, caller);

        var answer = Assert.IsType<AnswerOutcome>(run.Outcome);
        var summary = Assert.Single(answer.StepSummaries);
        Assert.Equal(new[] { "Brightwave Appliances Ltd" }, summary.Names);
        Assert.Equal(caller, run.Executor.CallerIds[2]);
        var lookup = run.Executor.Fetches[2];
        Assert.Contains(G1, lookup);
        Assert.DoesNotContain(G2, lookup);
    }

    /// <summary>
    /// Verifies that the name lookup asks only for the preview rows never the whole ID list.
    /// </summary>
    [Fact]
    public async Task TheNameLookup_AsksOnlyForThePreviewRows_NeverTheWholeIdList()
    {
        var many = Enumerable.Range(1, 40).Select(i => (1, Guid.Parse($"{i:x8}-0000-0000-0000-000000000000").ToString())).ToArray();
        var noTop = RankOpportunities.Replace(" top=\"1\"", "");

        var run = await Execute(noTop, new[] { Rows(many), TwoContacts });

        Assert.IsType<AnswerOutcome>(run.Outcome);
        var lookup = run.Executor.Fetches[2];
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(lookup, "<value>").Count);
    }

    /// <summary>
    /// Verifies that when the names cannot be resolved the answer still comes without names.
    /// </summary>
    [Fact]
    public async Task WhenTheNamesCannotBeResolved_TheAnswerStillComes_WithoutNames()
    {
        var run = await Execute(RankOpportunities, new[] { Rows((3, G1)), TwoContacts, "{\"value\":[]}" });

        var answer = Assert.IsType<AnswerOutcome>(run.Outcome);
        Assert.Null(Assert.Single(answer.StepSummaries).Names);
        Assert.Equal(2, answer.Table.Rows.Count);
    }
}
