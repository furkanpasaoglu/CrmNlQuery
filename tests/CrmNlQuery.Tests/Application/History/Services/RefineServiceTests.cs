using System.Text.Json;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.History.Services;

/// <summary>
/// Tests the refine service that runs a follow-up sentence on top of a stored result as its owner.
/// </summary>
public class RefineServiceTests
{
    private const string Fetch =
        "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"address1_city\" /></entity></fetch>";

    private sealed class NoopExecutor : IQueryExecutor
    {
        /// <summary>
        /// Test fake executor that returns a canned result table.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
            => Task.FromResult(ResultTableBuilder.Build(JsonDocument.Parse("[]").RootElement.Clone(), fetchXml));
    }

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Harness
    {
        public Clock Clock { get; } = new Clock();
        public IHistoryStore History { get; }
        public RefineService Service { get; }
        public List<AskRequest> Requests { get; } = new List<AskRequest>();
        public AskOutcome Next { get; set; } = Answer();

        public Harness()
        {
            History = new MemoryHistoryStore(() => Clock.Now);
            var results = new ResultService(History, TestMetadata.Create(), new NoopExecutor());
            var context = new ConversationContext(History, () => TimeSpan.FromMinutes(1), () => Clock.Now);
            Service = new RefineService(context, results, (request, ct) =>
            {
                Requests.Add(request);
                return Task.FromResult(Next);
            });
        }

        /// <summary>
        /// Test helper that stores a result for an owner with a question and query.
        /// </summary>
        public Task<string> Seed(Guid owner, string question = "Hamburg'daki şirketleri getir", string fetch = Fetch)
            => History.AddAsync(owner, question, "account", fetch);
    }

    private static AnswerOutcome Answer()
        => new AnswerOutcome("account", "Showing Account", Fetch, new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>()), false);

    /// <summary>
    /// Verifies that refine runs the new sentence with the stored query as context as the owner and forces continuation.
    /// </summary>
    [Fact]
    public async Task Refine_RunsTheNewSentence_WithTheStoredQueryAsContext_AsTheOwner_AndForcesTheContinuation()
    {
        var h = new Harness();
        var owner = Guid.NewGuid();
        var id = await h.Seed(owner);

        await h.Service.RefineAsync(id, owner, "sadece Almanya'dakiler", null);

        var request = Assert.Single(h.Requests);
        Assert.Equal("sadece Almanya'dakiler", request.Question);
        Assert.Equal(new PreviousQuery("Hamburg'daki şirketleri getir", "account", Fetch), request.Previous);
        Assert.True(request.ForceContinue);
        Assert.Equal(owner, request.Caller.UserId);
    }

    /// <summary>
    /// Verifies that refine ignores the conversation time window because the user is deliberately looking at that result.
    /// </summary>
    [Fact]
    public async Task Refine_IgnoresTheConversationTimeWindow_BecauseTheUserIsLookingAtThatResultOnPurpose()
    {
        var h = new Harness();
        var owner = Guid.NewGuid();
        var id = await h.Seed(owner);
        h.Clock.Now = h.Clock.Now.AddDays(3);

        await h.Service.RefineAsync(id, owner, "sadece Almanya", null);

        Assert.NotNull(Assert.Single(h.Requests).Previous);
    }

    /// <summary>
    /// Verifies that refine keeps the page's answers as they are.
    /// </summary>
    [Fact]
    public async Task Refine_KeepsThePagesAnswers_AsTheyAre()
    {
        var h = new Harness();
        var owner = Guid.NewGuid();
        var id = await h.Seed(owner);
        var answers = new Dictionary<string, string> { ["resolve:Denema"] = "id::Denema Sigorta", ["columns"] = "default" };

        await h.Service.RefineAsync(id, owner, "sadece Denema", answers);

        Assert.Equal("id::Denema Sigorta", h.Requests[0].Answers["resolve:Denema"]);
        Assert.Equal("default", h.Requests[0].Answers["columns"]);
    }

    /// <summary>
    /// Verifies that refine without answers sends no columns answer so the pipeline can inherit the columns.
    /// </summary>
    [Fact]
    public async Task Refine_WithoutAnswers_SendsNoColumnsAnswer_SoThePipelineCanInheritTheColumns()
    {
        var h = new Harness();
        var owner = Guid.NewGuid();
        var id = await h.Seed(owner);

        await h.Service.RefineAsync(id, owner, "sadece Almanya", null);

        Assert.False(h.Requests[0].Answers.ContainsKey("columns"));
    }

    /// <summary>
    /// Verifies that refine by someone else or with an unknown id is not found and runs nothing.
    /// </summary>
    [Fact]
    public async Task Refine_BySomeoneElse_OrUnknownId_IsNotFound_AndRunsNothing()
    {
        var h = new Harness();
        var id = await h.Seed(Guid.NewGuid());

        Assert.Null(await h.Service.RefineAsync(id, Guid.NewGuid(), "sadece Almanya", null));
        Assert.Null(await h.Service.RefineAsync("nope", Guid.NewGuid(), "sadece Almanya", null));
        Assert.Empty(h.Requests);
    }

    /// <summary>
    /// Verifies that refine with blank text is a failure.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Refine_BlankText_IsAFailure_AndRunsNothing(string? text)
    {
        var h = new Harness();
        var owner = Guid.NewGuid();
        var id = await h.Seed(owner);

        var outcome = await h.Service.RefineAsync(id, owner, text, null);

        Assert.IsType<FailureOutcome>(outcome);
        Assert.Empty(h.Requests);
    }

    /// <summary>
    /// Verifies that refine with overlong text is a failure.
    /// </summary>
    [Fact]
    public async Task Refine_OverlongText_IsAFailure()
    {
        var h = new Harness();
        var owner = Guid.NewGuid();
        var id = await h.Seed(owner);
        var outcome = await h.Service.RefineAsync(id, owner, new string('x', RefineService.MaxTextLength + 1), null);
        Assert.IsType<FailureOutcome>(outcome);
        Assert.Empty(h.Requests);
    }

    /// <summary>
    /// Verifies that a refine answer is saved as a new entry with the old one as parent and the old one stays.
    /// </summary>
    [Fact]
    public async Task Refine_Answer_IsSavedAsANewEntry_WithTheOldOneAsItsParent_AndTheOldOneStays()
    {
        var h = new Harness();
        var owner = Guid.NewGuid();
        var id = await h.Seed(owner);

        var outcome = await h.Service.RefineAsync(id, owner, "sadece Almanya", null);

        var answer = Assert.IsType<AnswerOutcome>(outcome);
        Assert.NotNull(answer.ResultId);
        Assert.NotEqual(id, answer.ResultId);
        var saved = await h.History.GetAsync(owner, answer.ResultId!);
        Assert.NotNull(saved);
        Assert.Equal("sadece Almanya", saved!.Question);
        Assert.Equal(id, saved.ParentId);
        Assert.NotNull(await h.History.GetAsync(owner, id));
    }

    /// <summary>
    /// Verifies that a question or failure from refine is returned as is without saving.
    /// </summary>
    [Fact]
    public async Task Refine_QuestionAndFailure_AreReturnedAsIs_WithoutSaving()
    {
        var h = new Harness();
        var owner = Guid.NewGuid();
        var id = await h.Seed(owner);
        var question = new QuestionOutcome("resolve", "resolve:Denema", "Which Denema?", new List<Choice>(), false);

        h.Next = question;
        Assert.Same(question, await h.Service.RefineAsync(id, owner, "sadece Denema", null));

        var failure = new FailureOutcome("nope");
        h.Next = failure;
        Assert.Same(failure, await h.Service.RefineAsync(id, owner, "sadece Denema", null));

        Assert.NotNull(await h.History.GetAsync(owner, id));
        Assert.Equal(0, (await h.History.CountConversationsAsync(owner)).Total);
    }
}
