using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Application.Suggestions.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.Suggestions.Services;

/// <summary>
/// Tests applying a follow-up suggestion without a model: the stored query is changed, validated, run as the caller and saved as a new result.
/// </summary>
public class SuggestionServiceTests
{
    private static AttributeInfo A(string name, string label, string type, params (int Value, string Label)[] options)
        => new AttributeInfo(name, label, type, options.Select(o => new OptionInfo(o.Value, o.Label)).ToList());

    private static readonly EntityInfo Opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo>
        {
            A("opportunityid", "Opportunity", "Uniqueidentifier"), A("name", "Topic", "String"),
            A("statecode", "Status", "State", (0, "Open"), (1, "Won"), (2, "Lost")),
            A("prioritycode", "Priority", "Picklist", (1, "High"), (2, "Normal")),
            A("createdon", "Created On", "DateTime"),
        },
        new List<RelationshipInfo>());

    private sealed class SpyExecutor : IQueryExecutor
    {
        public List<string> Executed { get; } = new List<string>();
        public Caller? LastCaller { get; private set; }

        /// <summary>
        /// Executes a query against a fake executor used by the test.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Executed.Add(fetchXml);
            LastCaller = caller;
            return Task.FromResult(new ResultTable(new List<string> { "g", "n" }, new List<IReadOnlyList<string>> { new List<string> { "High", "3" } }));
        }
    }

    private const string Plain = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /></entity></fetch>";

    private readonly MemoryHistoryStore _store = new MemoryHistoryStore();
    private readonly SpyExecutor _executor = new SpyExecutor();
    private readonly SuggestionService _service;
    private readonly Guid _me = Guid.NewGuid();

    public SuggestionServiceTests()
    {
        var metadata = new FakeMetadataSource(Opportunity);
        _service = new SuggestionService(_store, metadata, new PlanValidator(metadata), new ResultService(_store, metadata, _executor), new ChatService(_store));
    }

    private async Task<(string Id, string? Chat)> Seed(string question = "show opportunities")
    {
        var chat = await new ChatService(_store).EnsureAsync(_me, null, question);
        var id = await _store.AddAsync(_me, question, "opportunity", Plain, null, chat);
        return (id, chat);
    }

    /// <summary>
    /// Verifies that apply runs the changed query as the caller and publishes a child result in the same chat.
    /// </summary>
    [Fact]
    public async Task Apply_RunsTheChangedQueryAsTheCaller_AndPublishesAChildResultInTheSameChat()
    {
        var (id, chat) = await Seed();

        var outcome = await _service.ApplyAsync(id, _me, "group:prioritycode");

        var answer = Assert.IsType<AnswerOutcome>(outcome);
        var ran = Assert.Single(_executor.Executed);
        Assert.Contains("aggregate=\"true\"", ran);
        Assert.Contains("prioritycode", ran);
        Assert.Equal(_me, _executor.LastCaller!.UserId);
        Assert.Equal(chat, answer.ChatId);
        var saved = await _store.GetAsync(_me, answer.ResultId!);
        Assert.Equal(id, saved!.ParentId);
        Assert.Contains("(then: count by Priority)", saved.Question);
        Assert.StartsWith("show opportunities", saved.Question);
    }

    /// <summary>
    /// Verifies that a second suggestion replaces the earlier suffix instead of stacking them.
    /// </summary>
    [Fact]
    public async Task ASecondSuggestion_ReplacesTheEarlierSuffix_InsteadOfStackingThem()
    {
        var (id, _) = await Seed();
        var first = Assert.IsType<AnswerOutcome>(await _service.ApplyAsync(id, _me, "time:createdon:30d"));

        var (again, _) = await Seed(first.ResultId is null ? "x" : (await _store.GetAsync(_me, first.ResultId))!.Question);
        var second = Assert.IsType<AnswerOutcome>(await _service.ApplyAsync(again, _me, "narrow:statecode:1"));

        var question = (await _store.GetAsync(_me, second.ResultId!))!.Question;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(question, @"\(then:"));
        Assert.Contains("Status: Won", question);
    }

    /// <summary>
    /// Verifies that another user's result is not found and nothing runs.
    /// </summary>
    [Fact]
    public async Task AnotherUsersResult_IsNotFound_AndNothingRuns()
    {
        var (id, _) = await Seed();

        var outcome = await _service.ApplyAsync(id, Guid.NewGuid(), "group:prioritycode");

        Assert.Null(outcome);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that an invalid suggestion is rejected without running anything.
    /// </summary>
    [Fact]
    public async Task AnInvalidSuggestion_IsRejected_WithoutRunningAnything()
    {
        var (id, _) = await Seed();

        var outcome = await _service.ApplyAsync(id, _me, "narrow:statecode:99");

        var failure = Assert.IsType<FailureOutcome>(outcome);
        Assert.Equal("suggestion_invalid", failure.Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that ForAsync returns nothing for a chain answer.
    /// </summary>
    [Fact]
    public async Task ForAsync_ReturnsNothing_ForAChainAnswer()
    {
        var answer = new AnswerOutcome("opportunity", "u", Plain, new ResultTable(new[] { "name" }, new List<IReadOnlyList<string>> { new[] { "a" } }), false) { FromChain = true };

        Assert.Empty(await _service.ForAsync(answer));
    }
}
