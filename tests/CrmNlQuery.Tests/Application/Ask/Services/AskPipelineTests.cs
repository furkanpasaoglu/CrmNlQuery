using System.Text.Json;
using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Tests.Support;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Ask.Services;

/// <summary>
/// Tests the question pipeline end to end with fake analyzer, planner, searcher and executor.
/// </summary>
public class AskPipelineTests
{
    private sealed class FakeAnalyzer : IQuestionAnalyzer
    {
        private readonly QuestionAnalysis? _result;
        private readonly Exception? _throw;
        public int Calls { get; private set; }
        public FakeAnalyzer(QuestionAnalysis result) { _result = result; }
        public FakeAnalyzer(Exception ex) { _throw = ex; }
        /// <summary>
        /// Returns a canned question analysis for tests.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default)
        {
            Calls++;
            if (_throw is not null) throw _throw;
            return Task.FromResult(_result!);
        }
    }

    private sealed class FakeSearcher : IRecordSearcher
    {
        private readonly Dictionary<string, List<RecordMatch>> _byText;
        public Guid? LastCallerId { get; private set; }
        public FakeSearcher(Dictionary<string, List<RecordMatch>> byText) { _byText = byText; }
        /// <summary>
        /// Fake record searcher that records the caller's id and returns canned matches for the searched text, so tests can check impersonation and name resolution.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
        {
            LastCallerId = caller.UserIdOrNull();
            IReadOnlyList<RecordMatch> list = _byText.TryGetValue(text, out var found) ? found : new List<RecordMatch>();
            return Task.FromResult(list);
        }
    }

    private sealed class FakeViews : IDefaultViewSource
    {
        /// <summary>
        /// Fake view source that always returns the fixed default columns name and productnumber, so tests need no CRM saved-query lookup.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
        {
            IReadOnlyList<string> columns = new List<string> { "name", "productnumber" };
            return Task.FromResult(columns);
        }
    }

    private sealed class FakePlanner : IPlanGenerator
    {
        private readonly PlanOutcome _outcome;
        public PlanRequest? Last { get; private set; }
        public FakePlanner(PlanOutcome outcome) { _outcome = outcome; }
        /// <summary>
        /// Fake planner that remembers the last plan request it received and returns a preset outcome, letting tests inspect what the pipeline sent.
        /// </summary>
        public Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default)
        {
            Last = request;
            return Task.FromResult(_outcome);
        }
    }

    private sealed class FakeExecutor : IQueryExecutor
    {
        private readonly string _json;
        public int Calls { get; private set; }
        public Guid? LastCallerId { get; private set; }
        public FakeExecutor(string json) { _json = json; }
        /// <summary>
        /// Fake query executor that counts calls, records the caller id and builds a result table from canned JSON using the real table builder.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Calls++;
            LastCallerId = caller.UserIdOrNull();
            return Task.FromResult(ResultTableBuilder.Build(JsonDocument.Parse(_json).RootElement.GetProperty("value").Clone(), fetchXml));
        }
    }

    private sealed class ThrowingExecutor : IQueryExecutor
    {
        /// <summary>
        /// Throws to simulate a CRM failure.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            throw new CrmException("Cannot reach CRM: refused");
        }
    }

    private sealed class AggregateLimitExecutor : IQueryExecutor
    {
        /// <summary>
        /// Throws the CRM aggregate limit error to simulate an oversized aggregation.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            throw new CrmException("CRM returned 400: {\"error\":{\"code\":\"0x8004E023\"}}", 400, null, false, false, true);
        }
    }

    private sealed class PermissionDeniedExecutor : IQueryExecutor
    {
        /// <summary>
        /// Throws a permission denied error to simulate a user without access.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            throw new CrmException("You do not have permission to read Contract records.", 403, null, true);
        }
    }

    private const string ProductFetch = "<fetch top=\"500\"><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>";

    private static QuestionAnalysis Product(params string[] columns) =>
        new QuestionAnalysis("product", new List<string>(), new List<Mention>(), columns, false);

    private static AskPipeline Build(
        IQuestionAnalyzer analyzer, FakePlanner? planner = null, IQueryExecutor? executor = null, FakeSearcher? searcher = null,
        ILogger<AskPipeline>? logger = null, IKnowledgeSource? knowledge = null, IMetadataSource? metadata = null, IRecordSearcher? anySearcher = null,
        AnalysisCache? analysisCache = null)
    {
        return new AskPipeline(
            metadata ?? TestMetadata.Create(),
            analyzer,
            anySearcher ?? searcher ?? new FakeSearcher(new Dictionary<string, List<RecordMatch>>()),
            new ColumnPolicy(new FakeViews()),
            planner ?? new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>())),
            executor ?? new FakeExecutor("""{"value":[{"name":"Chair"}]}"""),
            decomposer: null,
            logger: logger,
            knowledge: knowledge,
            analysisCache: analysisCache);
    }

    private sealed class FakeKnowledge : IKnowledgeSource
    {
        private readonly IReadOnlyList<string> _notes;
        public List<string> Questions { get; } = new List<string>();
        public FakeKnowledge(params string[] notes) { _notes = notes; }
        /// <summary>
        /// Fake knowledge source that records each question it is asked and returns preset knowledge snippets, so tests can verify knowledge is passed along.
        /// </summary>
        public Task<IReadOnlyList<string>> FindAsync(string question, CancellationToken ct = default)
        {
            Questions.Add(question);
            return Task.FromResult(_notes);
        }
    }

    private sealed class KnowledgeAwareAnalyzer : IQuestionAnalyzer
    {
        public IReadOnlyList<string>? Seen { get; private set; }
        /// <summary>
        /// Analyzer overload without knowledge that delegates to the knowledge-aware overload with an empty knowledge list.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default)
            => AnalyzeAsync(question, entities, new List<string>(), ct);
        /// <summary>
        /// Stores the knowledge snippets it was given and returns a fixed product analysis, so tests can check what knowledge reached the analyzer.
        /// </summary>
        public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, CancellationToken ct = default)
        {
            Seen = knowledge;
            return Task.FromResult(Product("name"));
        }
    }

    /// <summary>
    /// Verifies that company knowledge is searched once per question and given to both the analyzer and the planner.
    /// </summary>
    [Fact]
    public async Task CompanyKnowledge_IsSearchedOncePerQuestion_AndGivenToBothTheAnalyzerAndThePlanner()
    {
        var source = new FakeKnowledge("KOBİ\nnew_musteriSegmenti = 2");
        var analyzer = new KnowledgeAwareAnalyzer();
        var planner = new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>()));

        await Build(analyzer, planner, knowledge: source).RunAsync(Ask("kobi müşteriler"));

        Assert.Equal(new[] { "kobi müşteriler" }, source.Questions);
        Assert.Equal(new[] { "KOBİ\nnew_musteriSegmenti = 2" }, analyzer.Seen);
        Assert.Equal(new[] { "KOBİ\nnew_musteriSegmenti = 2" }, planner.Last!.Knowledge);
    }

    /// <summary>
    /// Verifies that without a knowledge source the analyzer and planner get no knowledge.
    /// </summary>
    [Fact]
    public async Task WithoutAKnowledgeSource_TheAnalyzerAndPlannerGetNoKnowledge()
    {
        var analyzer = new KnowledgeAwareAnalyzer();
        var planner = new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>()));

        await Build(analyzer, planner).RunAsync(Ask("all products"));

        Assert.Empty(analyzer.Seen!);
        Assert.Empty(planner.Last!.Knowledge!);
    }

    /// <summary>
    /// Verifies that an analyzer that only knows the old method still works when knowledge is present.
    /// </summary>
    [Fact]
    public async Task AnAnalyzerThatOnlyKnowsTheOldMethod_StillWorks_WhenKnowledgeIsPresent()
    {
        var planner = new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>()));

        var outcome = await Build(new FakeAnalyzer(Product("name")), planner, knowledge: new FakeKnowledge("not")).RunAsync(Ask("q"));

        Assert.IsType<AnswerOutcome>(outcome);
        Assert.Single(planner.Last!.Knowledge!);
    }

    private static AskRequest Ask(string q, params (string Key, string Value)[] answers) =>
        new AskRequest(q, answers.ToDictionary(a => a.Key, a => a.Value), Caller.ForEvaluation());

    /// <summary>
    /// Verifies that blank question is failure.
    /// </summary>
    [Fact]
    public async Task BlankQuestion_IsFailure()
    {
        var outcome = await Build(new FakeAnalyzer(Product())).RunAsync(Ask("   "));
        Assert.IsType<FailureOutcome>(outcome);
    }

    /// <summary>
    /// Verifies that no columns asks column question and does not run query.
    /// </summary>
    [Fact]
    public async Task NoColumns_AsksColumnQuestion_AndDoesNotRunQuery()
    {
        var executor = new FakeExecutor("""{"value":[]}""");
        var outcome = await Build(new FakeAnalyzer(Product()), executor: executor).RunAsync(Ask("all products"));
        var q = Assert.IsType<QuestionOutcome>(outcome);
        Assert.Equal("columns", q.AnswerKey);
        Assert.Equal(0, executor.Calls);
    }

    /// <summary>
    /// Verifies that columns answered returns table.
    /// </summary>
    [Fact]
    public async Task ColumnsAnswered_ReturnsTable()
    {
        var planner = new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>()));
        var outcome = await Build(new FakeAnalyzer(Product()), planner).RunAsync(Ask("all products", ("columns", "default")));
        var answer = Assert.IsType<AnswerOutcome>(outcome);
        Assert.Equal(new[] { "name" }, answer.Table.Columns);
        Assert.Equal("Chair", answer.Table.Rows[0][0]);
        Assert.Equal(new[] { "name", "productnumber" }, planner.Last!.Columns);
        Assert.False(answer.Truncated);
    }

    /// <summary>
    /// Verifies that explicit columns skip question.
    /// </summary>
    [Fact]
    public async Task ExplicitColumns_SkipQuestion()
    {
        var outcome = await Build(new FakeAnalyzer(Product("id", "name"))).RunAsync(Ask("products id name"));
        Assert.IsType<AnswerOutcome>(outcome);
    }

    /// <summary>
    /// Verifies that aggregate skips column question.
    /// </summary>
    [Fact]
    public async Task Aggregate_SkipsColumnQuestion()
    {
        var analysis = new QuestionAnalysis("account", new List<string>(), new List<Mention>(), new List<string>(), true);
        var fetch = "<fetch aggregate=\"true\"><entity name=\"account\"><attribute name=\"accountid\" alias=\"count\" aggregate=\"count\" /></entity></fetch>";
        var planner = new FakePlanner(new PlanOutcome(true, fetch, new List<string>()));
        var executor = new FakeExecutor("""{"value":[{"count":19}]}""");
        var outcome = await Build(new FakeAnalyzer(analysis), planner, executor).RunAsync(Ask("how many accounts"));
        var answer = Assert.IsType<AnswerOutcome>(outcome);
        Assert.Equal("19", answer.Table.Rows[0][0]);
    }

    /// <summary>
    /// Verifies that unknown primary entity asks entity question.
    /// </summary>
    [Fact]
    public async Task UnknownPrimaryEntity_AsksEntityQuestion()
    {
        var analysis = new QuestionAnalysis("", new List<string>(), new List<Mention>(), new List<string>(), false);
        var outcome = await Build(new FakeAnalyzer(analysis)).RunAsync(Ask("gibberish"));
        var q = Assert.IsType<QuestionOutcome>(outcome);
        Assert.Equal("entity", q.Kind);
        Assert.Equal("", q.AnswerKey);
        Assert.Contains("(for example: Account, Product, Contract)", q.Text);
    }

    /// <summary>
    /// Verifies that the which kind hint uses only entities that exist at most three and nothing when none is common.
    /// </summary>
    [Fact]
    public void TheWhichKindHint_UsesOnlyEntitiesThatExist_AtMostThree_AndNothingWhenNoneIsCommon()
    {
        var some = new List<EntitySummary>
        {
            new EntitySummary("contract", "contracts", "Sözleşme"), new EntitySummary("opportunity", "opportunities", "Fırsat"),
            new EntitySummary("account", "accounts", "Firma"), new EntitySummary("contact", "contacts", "Kişi"), new EntitySummary("new_arac", "new_aracs", "Araç"),
        };

        Assert.Equal("I could not tell which kind of record you want. Please say it in your question (for example: Firma, Kişi, Fırsat).", UserMessages.WhichKindOfRecord(some));
        Assert.Equal("I could not tell which kind of record you want. Please say it in your question.",
            UserMessages.WhichKindOfRecord(new List<EntitySummary> { new EntitySummary("new_arac", "new_aracs", "Araç") }));
    }

    private static QuestionAnalysis ContractOf(string company) => new QuestionAnalysis(
        "contract", new List<string>(), new List<Mention> { new Mention(company, "account") }, new List<string> { "title" }, false);

    /// <summary>
    /// Verifies that ambiguous company asks which one.
    /// </summary>
    [Fact]
    public async Task AmbiguousCompany_AsksWhichOne()
    {
        var matches = new Dictionary<string, List<RecordMatch>>
        {
            ["Denema"] = new List<RecordMatch> { new RecordMatch(Guid.NewGuid(), "Denema Sigorta A.Ş."), new RecordMatch(Guid.NewGuid(), "Denema Sağlık Sigorta") }
        };
        var outcome = await Build(new FakeAnalyzer(ContractOf("Denema")), searcher: new FakeSearcher(matches)).RunAsync(Ask("Denema contracts"));
        var q = Assert.IsType<QuestionOutcome>(outcome);
        Assert.Equal("resolve:Denema", q.AnswerKey);
        Assert.Equal(2, q.Choices.Count);
        Assert.False(q.AllowFreeText);
    }

    private static async Task<QuestionOutcome> AskWith(params RecordMatch[] candidates)
    {
        var matches = new Dictionary<string, List<RecordMatch>> { ["Ornekova"] = candidates.ToList() };
        var outcome = await Build(new FakeAnalyzer(ContractOf("Ornekova")), searcher: new FakeSearcher(matches)).RunAsync(Ask("Ornekova contracts"));
        return Assert.IsType<QuestionOutcome>(outcome);
    }

    /// <summary>
    /// Verifies that same named candidates get distinct labels with date and short ID.
    /// </summary>
    [Fact]
    public async Task SameNamedCandidates_GetDistinctLabels_WithDateAndShortId()
    {
        var a = new RecordMatch(Guid.Parse("11111111-1111-1111-1111-111111aaaaaa"), "Örnekova Holding", new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc));
        var b = new RecordMatch(Guid.Parse("22222222-2222-2222-2222-222222bbbbbb"), "ORNEKOVA HOLDING", new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var q = await AskWith(a, b);

        Assert.Equal("Örnekova Holding (created 2026-03-12 · #aaaaaa)", q.Choices[0].Label);
        Assert.Equal("ORNEKOVA HOLDING (created 2025-01-05 · #bbbbbb)", q.Choices[1].Label);
        Assert.Equal($"{a.Id}::Örnekova Holding", q.Choices[0].Value);
    }

    /// <summary>
    /// Verifies that same named candidates without created on are separated by ID only.
    /// </summary>
    [Fact]
    public async Task SameNamedCandidates_WithoutCreatedOn_AreSeparatedByIdOnly()
    {
        var a = new RecordMatch(Guid.Parse("11111111-1111-1111-1111-111111aaaaaa"), "Örnekova Holding");
        var b = new RecordMatch(Guid.Parse("22222222-2222-2222-2222-222222bbbbbb"), "Örnekova Holding");
        var q = await AskWith(a, b);

        Assert.Equal("Örnekova Holding (#aaaaaa)", q.Choices[0].Label);
        Assert.Equal("Örnekova Holding (#bbbbbb)", q.Choices[1].Label);
    }

    /// <summary>
    /// Verifies that unique names keep their plain label even when someone else is duplicated.
    /// </summary>
    [Fact]
    public async Task UniqueNames_KeepTheirPlainLabel_EvenWhenSomeoneElseIsDuplicated()
    {
        var dupA = new RecordMatch(Guid.NewGuid(), "Örnekova Holding", new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc));
        var dupB = new RecordMatch(Guid.NewGuid(), "Örnekova Holding", new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var other = new RecordMatch(Guid.NewGuid(), "Örnekova Enerji", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var q = await AskWith(dupA, dupB, other);

        Assert.Equal("Örnekova Enerji", q.Choices[2].Label);
        Assert.NotEqual(q.Choices[0].Label, q.Choices[1].Label);
    }

    /// <summary>
    /// Verifies that ambiguous company answered continues with chosen record.
    /// </summary>
    [Fact]
    public async Task AmbiguousCompany_Answered_ContinuesWithChosenRecord()
    {
        var id = Guid.NewGuid();
        var planner = new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>()));
        var outcome = await Build(new FakeAnalyzer(ContractOf("Denema")), planner)
            .RunAsync(Ask("Denema contracts", ("resolve:Denema", $"{id}::Denema Sigorta A.Ş.")));
        Assert.IsType<AnswerOutcome>(outcome);
        Assert.Equal(id, planner.Last!.Resolved[0].Id);
        Assert.Equal("Denema Sigorta A.Ş.", planner.Last.Resolved[0].Name);
    }

    /// <summary>
    /// Verifies that single match is used without asking.
    /// </summary>
    [Fact]
    public async Task SingleMatch_IsUsedWithoutAsking()
    {
        var id = Guid.NewGuid();
        var matches = new Dictionary<string, List<RecordMatch>> { ["Örnekova"] = new List<RecordMatch> { new RecordMatch(id, "Örnekova Holding A.Ş.") } };
        var planner = new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>()));
        var outcome = await Build(new FakeAnalyzer(ContractOf("Örnekova")), planner, searcher: new FakeSearcher(matches)).RunAsync(Ask("Örnekova contracts"));
        Assert.IsType<AnswerOutcome>(outcome);
        Assert.Equal(id, planner.Last!.Resolved[0].Id);
    }

    private static AskRequest AskAs(Guid user, string q, params (string Key, string Value)[] answers) =>
        new AskRequest(q, answers.ToDictionary(a => a.Key, a => a.Value), Caller.User(user));

    /// <summary>
    /// Verifies that the answer round reuses the analysis when a cache is given.
    /// </summary>
    [Fact]
    public async Task TheAnswerRound_ReusesTheAnalysis_WhenACacheIsGiven()
    {
        var user = Guid.NewGuid();
        var analyzer = new FakeAnalyzer(Product());
        var pipeline = Build(analyzer, analysisCache: new AnalysisCache(() => TimeSpan.FromMinutes(10)));

        var first = await pipeline.RunAsync(AskAs(user, "q"));
        var second = await pipeline.RunAsync(AskAs(user, "q", ("columns", "default")));

        Assert.IsType<QuestionOutcome>(first);
        Assert.IsType<AnswerOutcome>(second);
        Assert.Equal(1, analyzer.Calls);
    }

    /// <summary>
    /// Verifies that without a cache every round analyses again.
    /// </summary>
    [Fact]
    public async Task WithoutACache_EveryRoundAnalysesAgain()
    {
        var user = Guid.NewGuid();
        var analyzer = new FakeAnalyzer(Product());
        var pipeline = Build(analyzer);

        await pipeline.RunAsync(AskAs(user, "q"));
        await pipeline.RunAsync(AskAs(user, "q", ("columns", "default")));

        Assert.Equal(2, analyzer.Calls);
    }

    /// <summary>
    /// Verifies that another user never gets the cached analysis.
    /// </summary>
    [Fact]
    public async Task AnotherUser_NeverGetsTheCachedAnalysis()
    {
        var analyzer = new FakeAnalyzer(Product());
        var pipeline = Build(analyzer, analysisCache: new AnalysisCache(() => TimeSpan.FromMinutes(10)));

        await pipeline.RunAsync(AskAs(Guid.NewGuid(), "q"));
        await pipeline.RunAsync(AskAs(Guid.NewGuid(), "q"));

        Assert.Equal(2, analyzer.Calls);
    }

    /// <summary>
    /// Verifies that a failed analysis is not cached.
    /// </summary>
    [Fact]
    public async Task AFailedAnalysis_IsNotCached()
    {
        var user = Guid.NewGuid();
        var analyzer = new FakeAnalyzer(new LlmOutputException("bad"));
        var pipeline = Build(analyzer, analysisCache: new AnalysisCache(() => TimeSpan.FromMinutes(10)));

        await pipeline.RunAsync(AskAs(user, "q"));
        await pipeline.RunAsync(AskAs(user, "q"));

        Assert.Equal(2, analyzer.Calls);
    }

    private sealed class EntitySearcher : IRecordSearcher
    {
        private readonly Dictionary<(string Entity, string Text), List<RecordMatch>> _byEntityAndText;
        public List<string> Searched { get; } = new List<string>();
        public Exception? ThrowFor { get; set; }
        public string? ThrowEntity { get; set; }
        public EntitySearcher(Dictionary<(string Entity, string Text), List<RecordMatch>> byEntityAndText) { _byEntityAndText = byEntityAndText; }
        /// <summary>
        /// Fake entity searcher that logs each entity:text search, can throw a configured exception for one entity, and otherwise returns canned matches per entity and text.
        /// </summary>
        public Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
        {
            Searched.Add(entity.LogicalName + ":" + text);
            if (ThrowFor is not null && ThrowEntity == entity.LogicalName) throw ThrowFor;
            IReadOnlyList<RecordMatch> list = _byEntityAndText.TryGetValue((entity.LogicalName, text), out var found) ? found : new List<RecordMatch>();
            return Task.FromResult(list);
        }
    }

    private static IMetadataSource WithContact() => new FakeMetadataSource(
        TestMetadata.Product(), TestMetadata.Account(), TestMetadata.Contract(), TestMetadata.ContractDetail(),
        new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
            new List<AttributeInfo> { new AttributeInfo("contactid", "contactid", "Uniqueidentifier", new List<OptionInfo>()), new AttributeInfo("fullname", "fullname", "String", new List<OptionInfo>()) },
            new List<RelationshipInfo>()));

    private static QuestionAnalysis ContractOfContact(string name) => new QuestionAnalysis(
        "contract", new List<string>(), new List<Mention> { new Mention(name, "contact") }, new List<string> { "title" }, false);

    /// <summary>
    /// Verifies that name guessed as contact but only an account has it uses the account.
    /// </summary>
    [Fact]
    public async Task NameGuessedAsContact_ButOnlyAnAccountHasIt_UsesTheAccount()
    {
        var id = Guid.NewGuid();
        var searcher = new EntitySearcher(new Dictionary<(string, string), List<RecordMatch>> { [("account", "Örnekova")] = new List<RecordMatch> { new RecordMatch(id, "Örnekova Holding A.Ş.") } });
        var planner = new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>()));

        var outcome = await Build(new FakeAnalyzer(ContractOfContact("Örnekova")), planner, metadata: WithContact(), anySearcher: searcher).RunAsync(Ask("q"));

        Assert.IsType<AnswerOutcome>(outcome);
        Assert.Equal(new[] { "contact:Örnekova", "account:Örnekova" }, searcher.Searched);
        Assert.Equal("account", planner.Last!.Resolved[0].Entity);
        Assert.Equal(id, planner.Last.Resolved[0].Id);
    }

    /// <summary>
    /// Verifies that name guessed as account but only a contact has it uses the contact.
    /// </summary>
    [Fact]
    public async Task NameGuessedAsAccount_ButOnlyAContactHasIt_UsesTheContact()
    {
        var id = Guid.NewGuid();
        var searcher = new EntitySearcher(new Dictionary<(string, string), List<RecordMatch>> { [("contact", "Kaya")] = new List<RecordMatch> { new RecordMatch(id, "Ada Kaya") } });
        var planner = new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>()));

        var outcome = await Build(new FakeAnalyzer(ContractOf("Kaya")), planner, metadata: WithContact(), anySearcher: searcher).RunAsync(Ask("q"));

        Assert.IsType<AnswerOutcome>(outcome);
        Assert.Equal("contact", planner.Last!.Resolved[0].Entity);
    }

    /// <summary>
    /// Verifies that the guessed type wins when it has a match the other type is not searched.
    /// </summary>
    [Fact]
    public async Task TheGuessedTypeWins_WhenItHasAMatch_TheOtherTypeIsNotSearched()
    {
        var searcher = new EntitySearcher(new Dictionary<(string, string), List<RecordMatch>> { [("account", "Örnekova")] = new List<RecordMatch> { new RecordMatch(Guid.NewGuid(), "Örnekova Holding A.Ş.") } });

        await Build(new FakeAnalyzer(ContractOf("Örnekova")), metadata: WithContact(), anySearcher: searcher).RunAsync(Ask("q"));

        Assert.Equal(new[] { "account:Örnekova" }, searcher.Searched);
    }

    /// <summary>
    /// Verifies that not found in either type is a failure that names both types.
    /// </summary>
    [Fact]
    public async Task NotFoundInEitherType_IsAFailure_ThatNamesBothTypes()
    {
        var searcher = new EntitySearcher(new Dictionary<(string, string), List<RecordMatch>>());

        var outcome = await Build(new FakeAnalyzer(ContractOfContact("Zorlu Holding")), metadata: WithContact(), anySearcher: searcher).RunAsync(Ask("q"));

        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("not_found", failure.Kind);
        Assert.Contains("Zorlu Holding", failure.Message);
        Assert.Contains("Contact", failure.Message);
        Assert.Contains("Account", failure.Message);
    }

    /// <summary>
    /// Verifies that several accounts match the user is asked and the answer is used as an account.
    /// </summary>
    [Fact]
    public async Task SeveralAccountsMatch_TheUserIsAsked_AndTheAnswerIsUsedAsAnAccount()
    {
        var first = Guid.NewGuid();
        var searcher = new EntitySearcher(new Dictionary<(string, string), List<RecordMatch>>
        {
            [("account", "Denema")] = new List<RecordMatch> { new RecordMatch(first, "Denema Sigorta A.Ş."), new RecordMatch(Guid.NewGuid(), "Denema Sağlık Sigorta") },
        });

        var question = Assert.IsType<QuestionOutcome>(await Build(new FakeAnalyzer(ContractOfContact("Denema")), metadata: WithContact(), anySearcher: searcher).RunAsync(Ask("q")));
        Assert.Equal(2, question.Choices.Count);

        var planner = new FakePlanner(new PlanOutcome(true, ProductFetch, new List<string>()));
        var answered = await Build(new FakeAnalyzer(ContractOfContact("Denema")), planner, metadata: WithContact(), anySearcher: searcher)
            .RunAsync(Ask("q", (question.AnswerKey, question.Choices[0].Value)));

        Assert.IsType<AnswerOutcome>(answered);
        Assert.Equal("account", planner.Last!.Resolved[0].Entity);
        Assert.Equal(first, planner.Last.Resolved[0].Id);
    }

    /// <summary>
    /// Verifies that a permission problem on the other type does not change the ordinary not found answer.
    /// </summary>
    [Fact]
    public async Task APermissionProblemOnTheOtherType_DoesNotChangeTheOrdinaryNotFoundAnswer()
    {
        var searcher = new EntitySearcher(new Dictionary<(string, string), List<RecordMatch>>())
        {
            ThrowEntity = "account",
            ThrowFor = new CrmException("denied", isPermissionDenied: true),
        };

        var outcome = await Build(new FakeAnalyzer(ContractOfContact("Zorlu Holding")), metadata: WithContact(), anySearcher: searcher).RunAsync(Ask("q"));

        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("not_found", failure.Kind);
    }

    /// <summary>
    /// Verifies that other entity types get no fallback.
    /// </summary>
    [Fact]
    public async Task OtherEntityTypes_GetNoFallback()
    {
        var searcher = new EntitySearcher(new Dictionary<(string, string), List<RecordMatch>>());
        var analysis = new QuestionAnalysis("contract", new List<string>(), new List<Mention> { new Mention("Chair", "product") }, new List<string> { "title" }, false);

        var outcome = await Build(new FakeAnalyzer(analysis), metadata: WithContact(), anySearcher: searcher).RunAsync(Ask("q"));

        Assert.Equal("not_found", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Equal(new[] { "product:Chair" }, searcher.Searched);
    }

    /// <summary>
    /// Verifies that no match at all is failure not guess.
    /// </summary>
    [Fact]
    public async Task NoMatchAtAll_IsFailure_NotGuess()
    {
        var outcome = await Build(new FakeAnalyzer(ContractOf("Zorlu Holding"))).RunAsync(Ask("Zorlu contracts"));
        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Contains("Zorlu Holding", failure.Message);
    }

    /// <summary>
    /// Verifies that no match but first word matches asks did you mean.
    /// </summary>
    [Fact]
    public async Task NoMatch_ButFirstWordMatches_AsksDidYouMean()
    {
        var matches = new Dictionary<string, List<RecordMatch>>
        {
            ["Zorlu"] = new List<RecordMatch> { new RecordMatch(Guid.NewGuid(), "Zorlu Enerji") }
        };
        var outcome = await Build(new FakeAnalyzer(ContractOf("Zorlu Holding")), searcher: new FakeSearcher(matches)).RunAsync(Ask("q"));
        var q = Assert.IsType<QuestionOutcome>(outcome);
        Assert.Equal("resolve:Zorlu Holding", q.AnswerKey);
        Assert.Single(q.Choices);
    }

    /// <summary>
    /// Verifies that invalid plan is failure and never reaches CRM.
    /// </summary>
    [Fact]
    public async Task InvalidPlan_IsFailure_AndNeverReachesCrm()
    {
        var executor = new FakeExecutor("""{"value":[]}""");
        var planner = new FakePlanner(new PlanOutcome(false, null, new List<string> { "Attribute 'x' does not exist" }));
        var outcome = await Build(new FakeAnalyzer(Product("name")), planner, executor).RunAsync(Ask("q"));
        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Contains("does not exist", failure.Message);
        Assert.Equal(0, executor.Calls);
    }

    /// <summary>
    /// Verifies that LLM down is failure with message not exception.
    /// </summary>
    [Fact]
    public async Task LlmDown_IsFailureWithMessage_NotException()
    {
        var outcome = await Build(new FakeAnalyzer(new LlmUnavailableException("Cannot reach Ollama at http://10.0.0.7:11434"))).RunAsync(Ask("q"));
        var message = Assert.IsType<FailureOutcome>(outcome).Message;
        Assert.Equal(UserMessages.ModelUnavailable, message);
        Assert.DoesNotContain("10.0.0.7", message);
    }

    /// <summary>
    /// Verifies that garbage model output is failure with message not exception.
    /// </summary>
    [Fact]
    public async Task GarbageModelOutput_IsFailureWithMessage_NotException()
    {
        var outcome = await Build(new FakeAnalyzer(new LlmOutputException("bad json"))).RunAsync(Ask("q"));
        Assert.IsType<FailureOutcome>(outcome);
    }

    /// <summary>
    /// Verifies that unexpected exception is failure with message not exception.
    /// </summary>
    [Fact]
    public async Task UnexpectedException_IsFailureWithMessage_NotException()
    {
        var outcome = await Build(new FakeAnalyzer(new InvalidOperationException("surprise"))).RunAsync(Ask("q"));
        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Contains("Unexpected", failure.Message);
    }

    /// <summary>
    /// Verifies that caller cancellation still propagates.
    /// </summary>
    [Fact]
    public async Task CallerCancellation_StillPropagates()
    {
        var pipeline = Build(new FakeAnalyzer(new OperationCanceledException()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.RunAsync(Ask("q")));
    }

    /// <summary>
    /// Verifies that CRM down is failure with message not exception.
    /// </summary>
    [Fact]
    public async Task CrmDown_IsFailureWithMessage_NotException()
    {
        var outcome = await Build(new FakeAnalyzer(Product("name")), executor: new ThrowingExecutor()).RunAsync(Ask("q"));
        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Contains("CRM", failure.Message);
    }

    /// <summary>
    /// Verifies that when the CRM aggregate limit is exceeded the user is told to narrow the question.
    /// </summary>
    [Fact]
    public async Task WhenTheCrmAggregateLimitIsExceeded_TheUserIsToldToNarrowTheQuestion()
    {
        var outcome = await Build(new FakeAnalyzer(Product("name")), executor: new AggregateLimitExecutor()).RunAsync(Ask("q"));

        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("aggregate_limit", failure.Kind);
        Assert.Equal(UserMessages.AggregateTooBig, failure.Message);
        Assert.DoesNotContain("0x8004", failure.Message);
    }

    /// <summary>
    /// Verifies that failure kinds are stable names for the usage dashboard.
    /// </summary>
    [Fact]
    public async Task FailureKinds_AreStableNames_ForTheUsageDashboard()
    {
        Assert.Equal("empty_question", Kind(await Build(new FakeAnalyzer(Product())).RunAsync(Ask("  "))));
        Assert.Equal("llm_unavailable", Kind(await Build(new FakeAnalyzer(new LlmUnavailableException("down"))).RunAsync(Ask("q"))));
        Assert.Equal("llm_output", Kind(await Build(new FakeAnalyzer(new LlmOutputException("bad"))).RunAsync(Ask("q"))));
        Assert.Equal("unexpected", Kind(await Build(new FakeAnalyzer(new InvalidOperationException("x"))).RunAsync(Ask("q"))));
        Assert.Equal("crm_error", Kind(await Build(new FakeAnalyzer(Product("name")), executor: new ThrowingExecutor()).RunAsync(Ask("q"))));
        Assert.Equal("permission", Kind(await Build(new FakeAnalyzer(Product("name")), executor: new PermissionDeniedExecutor()).RunAsync(Ask("q"))));
        Assert.Equal("impersonation", Kind(await Build(new FakeAnalyzer(Product("name")), executor: new ImpersonationDeniedExecutor()).RunAsync(Ask("q"))));
        var invalid = new FakePlanner(new PlanOutcome(false, null, new List<string> { "bad plan" }));
        Assert.Equal("invalid_plan", Kind(await Build(new FakeAnalyzer(Product("name")), invalid).RunAsync(Ask("q"))));
        Assert.Equal("not_found", Kind(await Build(new FakeAnalyzer(ContractOf("Zorlu Holding"))).RunAsync(Ask("q"))));
    }

    private static string Kind(AskOutcome outcome) => Assert.IsType<FailureOutcome>(outcome).Kind;

    private sealed class CapturingLogger : ILogger<AskPipeline>
    {
        public List<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = new List<(LogLevel, string, Exception?)>();
        /// <summary>
        /// Returns no scope for the capturing test logger.
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>
        /// Reports every log level as enabled.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>
        /// Captures the log entry for assertions in tests.
        /// </summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    /// <summary>
    /// Verifies that unexpected errors are logged as errors with the real exception.
    /// </summary>
    [Fact]
    public async Task UnexpectedErrors_AreLoggedAsErrors_WithTheRealException()
    {
        var logger = new CapturingLogger();
        await Build(new FakeAnalyzer(new InvalidOperationException("surprise")), logger: logger).RunAsync(Ask("q"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Error);
        Assert.Contains("unexpected", entry.Message);
    }

    /// <summary>
    /// Verifies that context overflow gives a fixed message without token numbers and a logged warning.
    /// </summary>
    [Fact]
    public async Task ContextOverflow_GivesAFixedMessage_WithoutTokenNumbers_AndALoggedWarning()
    {
        var logger = new CapturingLogger();
        var outcome = await Build(new FakeAnalyzer(new PromptTooLongException(PromptTooLongException.PromptCause, 16380, 2, 16384)), logger: logger).RunAsync(Ask("q"));

        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("context_overflow", failure.Kind);
        Assert.Equal(UserMessages.ContextOverflow, failure.Message);
        Assert.DoesNotContain("16384", failure.Message);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
        Assert.Contains("context_overflow", logger.Entries[0].Message);
    }

    /// <summary>
    /// Verifies that model and CRM problems are logged as warnings.
    /// </summary>
    [Fact]
    public async Task ModelAndCrmProblems_AreLoggedAsWarnings()
    {
        var logger = new CapturingLogger();
        await Build(new FakeAnalyzer(new LlmUnavailableException("down")), logger: logger).RunAsync(Ask("q"));
        await Build(new FakeAnalyzer(new LlmOutputException("bad")), logger: logger).RunAsync(Ask("q"));
        await Build(new FakeAnalyzer(Product("name")), executor: new ThrowingExecutor(), logger: logger).RunAsync(Ask("q"));

        Assert.Equal(3, logger.Entries.Count);
        Assert.All(logger.Entries, e => Assert.Equal(LogLevel.Warning, e.Level));
        Assert.Contains("llm_unavailable", logger.Entries[0].Message);
        Assert.Contains("llm_output", logger.Entries[1].Message);
        Assert.Contains("crm_error", logger.Entries[2].Message);
    }

    /// <summary>
    /// Verifies that user level outcomes are not logged as problems.
    /// </summary>
    [Fact]
    public async Task UserLevelOutcomes_AreNotLoggedAsProblems()
    {
        var logger = new CapturingLogger();
        await Build(new FakeAnalyzer(Product("name")), executor: new PermissionDeniedExecutor(), logger: logger).RunAsync(Ask("q"));
        await Build(new FakeAnalyzer(ContractOf("Zorlu Holding")), logger: logger).RunAsync(Ask("q"));

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    /// <summary>
    /// Verifies that the log carries the request ID but never the question text.
    /// </summary>
    [Fact]
    public async Task TheLog_CarriesTheRequestId_ButNeverTheQuestionText()
    {
        var logger = new CapturingLogger();
        var id = Guid.NewGuid();
        using (RequestContext.Begin(id))
        {
            await Build(new FakeAnalyzer(new InvalidOperationException("boom")), logger: logger)
                .RunAsync(Ask("SECRET-QUESTION-TEXT about Zorlu Holding"));
        }

        var entry = Assert.Single(logger.Entries);
        Assert.Contains(id.ToString(), entry.Message);
        Assert.DoesNotContain("SECRET-QUESTION-TEXT", entry.Message);
        Assert.DoesNotContain("Zorlu", entry.Message);
    }

    /// <summary>
    /// Verifies that caller ID reaches the query executor and the company search.
    /// </summary>
    [Fact]
    public async Task CallerId_ReachesTheQueryExecutor_AndTheCompanySearch()
    {
        var caller = Guid.NewGuid();
        var executor = new FakeExecutor("""{"value":[{"name":"Chair"}]}""");
        var matches = new Dictionary<string, List<RecordMatch>> { ["Örnekova"] = new List<RecordMatch> { new RecordMatch(Guid.NewGuid(), "Örnekova Holding A.Ş.") } };
        var searcher = new FakeSearcher(matches);
        var pipeline = Build(new FakeAnalyzer(ContractOf("Örnekova")), executor: executor, searcher: searcher);

        var outcome = await pipeline.RunAsync(new AskRequest("Örnekova contracts", new Dictionary<string, string>(), Caller.User(caller)));

        Assert.IsType<AnswerOutcome>(outcome);
        Assert.Equal(caller, searcher.LastCallerId);
        Assert.Equal(caller, executor.LastCallerId);
    }

    /// <summary>
    /// Verifies that without caller ID nothing is impersonated.
    /// </summary>
    [Fact]
    public async Task WithoutCallerId_NothingIsImpersonated()
    {
        var executor = new FakeExecutor("""{"value":[{"name":"Chair"}]}""");
        await Build(new FakeAnalyzer(Product("name")), executor: executor).RunAsync(Ask("q"));
        Assert.Null(executor.LastCallerId);
    }

    private sealed class ImpersonationDeniedExecutor : IQueryExecutor
    {
        /// <summary>
        /// Throws an impersonation denied error to simulate a service account without impersonation rights.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            throw new CrmException("The service account is not allowed to act on behalf of users.", 401, null, false, true);
        }
    }

    /// <summary>
    /// Verifies that impersonation denied is shown as is for the administrator to fix.
    /// </summary>
    [Fact]
    public async Task ImpersonationDenied_IsShownAsIs_ForTheAdministratorToFix()
    {
        var outcome = await Build(new FakeAnalyzer(Product("name")), executor: new ImpersonationDeniedExecutor()).RunAsync(Ask("q"));
        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("The service account is not allowed to act on behalf of users.", failure.Message);
    }

    /// <summary>
    /// Verifies that permission denied is shown as is without the CRM error prefix.
    /// </summary>
    [Fact]
    public async Task PermissionDenied_IsShownAsIs_WithoutTheCrmErrorPrefix()
    {
        var outcome = await Build(new FakeAnalyzer(Product("name")), executor: new PermissionDeniedExecutor()).RunAsync(Ask("q"));
        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("You do not have permission to read Contract records.", failure.Message);
    }

    /// <summary>
    /// Verifies that five hundred rows are flagged as truncated.
    /// </summary>
    [Fact]
    public async Task FiveHundredRows_AreFlaggedAsTruncated()
    {
        var rows = string.Join(",", Enumerable.Range(0, 500).Select(i => $"{{\"name\":\"P{i}\"}}"));
        var executor = new FakeExecutor("{\"value\":[" + rows + "]}");
        var answer = Assert.IsType<AnswerOutcome>(await Build(new FakeAnalyzer(Product("name")), executor: executor).RunAsync(Ask("q")));
        Assert.True(answer.Truncated);
    }

    private const string AggregateFetch =
        "<fetch aggregate=\"true\"><entity name=\"product\"><attribute name=\"productid\" alias=\"n\" aggregate=\"count\" /></entity></fetch>";

    private static string Rows(string json, int count) =>
        "{\"value\":[" + string.Join(",", Enumerable.Range(0, count).Select(_ => json)) + "]}";

    /// <summary>
    /// Verifies that truncation follows the fetch XML, not the analysis, and aggregate fetch.
    /// </summary>
    [Fact]
    public async Task Truncation_FollowsTheFetchXml_NotTheAnalysis_AggregateFetch()
    {
        var planner = new FakePlanner(new PlanOutcome(true, AggregateFetch, new List<string>()));
        var executor = new FakeExecutor(Rows("{\"n\":1}", 500));
        var answer = Assert.IsType<AnswerOutcome>(await Build(new FakeAnalyzer(Product("name")), planner, executor).RunAsync(Ask("q")));
        Assert.False(answer.Truncated);
    }

    /// <summary>
    /// Verifies that truncation follows the fetch XML, not the analysis, and plain fetch.
    /// </summary>
    [Fact]
    public async Task Truncation_FollowsTheFetchXml_NotTheAnalysis_PlainFetch()
    {
        var analysis = new QuestionAnalysis("product", new List<string>(), new List<Mention>(), new List<string>(), true);
        var executor = new FakeExecutor(Rows("{\"name\":\"P\"}", 500));
        var answer = Assert.IsType<AnswerOutcome>(await Build(new FakeAnalyzer(analysis), executor: executor).RunAsync(Ask("q")));
        Assert.True(answer.Truncated);
    }

    /// <summary>
    /// Verifies that a truncated answer carries narrow hints from the entitys status field.
    /// </summary>
    [Fact]
    public async Task ATruncatedAnswer_CarriesNarrowHints_FromTheEntitysStatusField()
    {
        var rows = string.Join(",", Enumerable.Range(0, 500).Select(i => $"{{\"name\":\"P{i}\"}}"));
        var executor = new FakeExecutor("{\"value\":[" + rows + "]}");

        var answer = Assert.IsType<AnswerOutcome>(await Build(new FakeAnalyzer(Product("name")), executor: executor).RunAsync(Ask("q")));

        var hint = Assert.Single(answer.NarrowHints);
        Assert.Equal("statecode", hint.Attribute);
        Assert.Equal("options", hint.Kind);
        Assert.Equal(new[] { "Active", "Retired" }, hint.Options.ToArray());
    }

    /// <summary>
    /// Verifies that a non truncated answer has no narrow hints.
    /// </summary>
    [Fact]
    public async Task ANonTruncatedAnswer_HasNoNarrowHints()
    {
        var executor = new FakeExecutor("{\"value\":[{\"name\":\"P\"}]}");

        var answer = Assert.IsType<AnswerOutcome>(await Build(new FakeAnalyzer(Product("name")), executor: executor).RunAsync(Ask("q")));

        Assert.Empty(answer.NarrowHints);
    }

    /// <summary>
    /// Verifies that four hundred ninety nine rows are not truncated.
    /// </summary>
    [Fact]
    public async Task FourHundredNinetyNineRows_AreNotTruncated()
    {
        var rows = string.Join(",", Enumerable.Range(0, 499).Select(i => $"{{\"name\":\"P{i}\"}}"));
        var executor = new FakeExecutor("{\"value\":[" + rows + "]}");
        var answer = Assert.IsType<AnswerOutcome>(await Build(new FakeAnalyzer(Product("name")), executor: executor).RunAsync(Ask("q")));
        Assert.False(answer.Truncated);
    }
}
