using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Application.Explain.Services;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.Explain.Services;

/// <summary>
/// Tests removing or changing an explanation chip without the model, re-validating and running as the caller.
/// </summary>
public class ExplainEditServiceTests
{
    private static AttributeInfo A(string name, string label, string type, params (int Value, string Label)[] options)
        => new AttributeInfo(name, label, type, options.Select(o => new OptionInfo(o.Value, o.Label)).ToList());

    private static readonly EntityInfo Opportunity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo>
        {
            A("opportunityid", "Opportunity", "Uniqueidentifier"), A("name", "Topic", "String"),
            A("statecode", "Status", "State", (0, "Open"), (1, "Won"), (2, "Lost")),
            A("address1_city", "City", "String"), A("estimatedvalue", "Est. Revenue", "Money"),
            A("createdon", "Created On", "DateTime"), A("customerid", "Potential Customer", "Customer"),
            A("numberofemployees", "Employees", "Integer"),
        },
        new List<RelationshipInfo>());

    private sealed class SpyExecutor : IQueryExecutor
    {
        public List<string> Executed { get; } = new List<string>();
        public Caller? LastCaller { get; private set; }

        /// <summary>
        /// Executor spy that records the query and caller it was asked to run.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Executed.Add(fetchXml);
            LastCaller = caller;
            return Task.FromResult(new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>> { new List<string> { "Deal" } }));
        }
    }

    private const string Base =
        "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><filter>" +
        "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" />" +
        "<condition attribute=\"address1_city\" operator=\"like\" value=\"%Ist%\" />" +
        "<condition attribute=\"estimatedvalue\" operator=\"gt\" value=\"1000\" />" +
        "<condition attribute=\"createdon\" operator=\"last-x-days\" value=\"30\" />" +
        "</filter></entity></fetch>";

    private readonly MemoryHistoryStore _store = new MemoryHistoryStore();
    private readonly SpyExecutor _executor = new SpyExecutor();
    private readonly ExplainEditService _service;
    private readonly Guid _me = Guid.NewGuid();

    public ExplainEditServiceTests()
    {
        var metadata = new FakeMetadataSource(Opportunity);
        var results = new ResultService(_store, metadata, _executor);
        _service = new ExplainEditService(_store, new QueryExplainer(metadata, () => new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero)),
            new PlanValidator(metadata), results, new ChatService(_store));
    }

    private async Task<(string Id, string? Chat)> Seed(string fetchXml, string question = "open deals in Istanbul")
    {
        var chat = await new ChatService(_store).EnsureAsync(_me, null, question);
        var id = await _store.AddAsync(_me, question, "opportunity", fetchXml, null, chat);
        return (id, chat);
    }

    /// <summary>
    /// Verifies that removing drops only that condition runs the edited query as the caller and publishes a child in the same chat.
    /// </summary>
    [Fact]
    public async Task Removing_DropsOnlyThatCondition_RunsTheEditedQueryAsTheCaller_AndPublishesAChildInTheSameChat()
    {
        var (id, chat) = await Seed(Base);

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f1", "remove", null));

        var answer = Assert.IsType<AnswerOutcome>(outcome);
        var ran = Assert.Single(_executor.Executed);
        Assert.DoesNotContain("statecode", ran);
        Assert.Contains("address1_city", ran);
        Assert.Contains("estimatedvalue", ran);
        Assert.Equal(_me, _executor.LastCaller!.UserId);
        Assert.NotEqual(id, answer.ResultId);
        Assert.Equal(chat, answer.ChatId);
        var saved = await _store.GetAsync(_me, answer.ResultId!);
        Assert.Equal(id, saved!.ParentId);
        Assert.Equal(chat, saved.ConversationId);
        Assert.Contains("edited", saved.Question);
        Assert.Contains("Status", saved.Question);
        Assert.StartsWith("open deals in Istanbul", saved.Question);
    }

    /// <summary>
    /// Verifies that removing the last condition also removes the empty filter.
    /// </summary>
    [Fact]
    public async Task RemovingTheLastCondition_AlsoRemovesTheEmptyFilter()
    {
        var (id, _) = await Seed("<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter></entity></fetch>");

        await _service.EditAsync(id, _me, new ExplainEdit("f1", "remove", null));

        Assert.DoesNotContain("<filter", Assert.Single(_executor.Executed));
    }

    /// <summary>
    /// Verifies that a date filter can be removed.
    /// </summary>
    [Fact]
    public async Task ADateFilter_CanBeRemoved()
    {
        var (id, _) = await Seed(Base);

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f4", "remove", null));

        Assert.IsType<AnswerOutcome>(outcome);
        Assert.DoesNotContain("createdon", Assert.Single(_executor.Executed));
    }

    /// <summary>
    /// Verifies that setting an option replaces the condition value.
    /// </summary>
    [Fact]
    public async Task SettingAnOption_ReplacesTheValue()
    {
        var (id, _) = await Seed(Base);

        await _service.EditAsync(id, _me, new ExplainEdit("f1", "set", "1"));

        Assert.Contains("attribute=\"statecode\" operator=\"eq\" value=\"1\"", Assert.Single(_executor.Executed));
    }

    /// <summary>
    /// Verifies that an option not in the list is rejected and nothing runs.
    /// </summary>
    [Theory]
    [InlineData("7")]
    [InlineData("Open")]
    [InlineData("")]
    public async Task AnOptionNotInTheList_IsRejected_AndNothingRuns(string value)
    {
        var (id, _) = await Seed(Base);

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f1", "set", value));

        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that setting a number uses the invariant format and rejects garbage.
    /// </summary>
    [Fact]
    public async Task SettingANumber_UsesTheInvariantFormat_AndRejectsGarbage()
    {
        var (id, _) = await Seed(Base);

        await _service.EditAsync(id, _me, new ExplainEdit("f3", "set", "2500.5"));
        var bad = await _service.EditAsync(id, _me, new ExplainEdit("f3", "set", "abc"));

        Assert.Contains("operator=\"gt\" value=\"2500.5\"", Assert.Single(_executor.Executed));
        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(bad).Kind);
    }

    /// <summary>
    /// Verifies that setting text keeps the like wildcard shape and strips percent signs.
    /// </summary>
    [Fact]
    public async Task SettingText_KeepsTheLikeWildcardShape_AndStripsPercentSigns()
    {
        var (id, _) = await Seed(Base);

        await _service.EditAsync(id, _me, new ExplainEdit("f2", "set", "An%kara"));

        Assert.Contains("operator=\"like\" value=\"%Ankara%\"", Assert.Single(_executor.Executed));
    }

    /// <summary>
    /// Verifies that empty or wildcard only text is rejected.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("%%%")]
    public async Task EmptyOrWildcardOnlyText_IsRejected(string value)
    {
        var (id, _) = await Seed(Base);

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f2", "set", value));

        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that text that is too long or has control characters is rejected.
    /// </summary>
    [Fact]
    public async Task TooLongTextOrControlCharacters_AreRejected()
    {
        var (id, _) = await Seed(Base);

        var tooLong = await _service.EditAsync(id, _me, new ExplainEdit("f2", "set", new string('a', ExplainEditService.MaxTextLength + 1)));
        var control = await _service.EditAsync(id, _me, new ExplainEdit("f2", "set", "bad\u0007text"));

        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(tooLong).Kind);
        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(control).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that a date filter can only be removed, not changed.
    /// </summary>
    [Fact]
    public async Task ADateFilter_CannotBeChanged_OnlyRemoved()
    {
        var (id, _) = await Seed(Base);

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f4", "set", "5"));

        Assert.Equal("edit_not_allowed", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that an integer field rejects decimals and exponents as an invalid edit rather than a CRM error.
    /// </summary>
    [Fact]
    public async Task AnIntegerField_RejectsDecimalsAndExponents_AsEditInvalid_NotACrmError()
    {
        var (id, _) = await Seed("<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><filter><condition attribute=\"numberofemployees\" operator=\"gt\" value=\"10\" /></filter></entity></fetch>");

        var decimals = await _service.EditAsync(id, _me, new ExplainEdit("f1", "set", "1.5"));
        var exponent = await _service.EditAsync(id, _me, new ExplainEdit("f1", "set", "1e3"));
        var tooBig = await _service.EditAsync(id, _me, new ExplainEdit("f1", "set", "99999999999999999999"));
        var ok = await _service.EditAsync(id, _me, new ExplainEdit("f1", "set", "12"));

        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(decimals).Kind);
        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(exponent).Kind);
        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(tooBig).Kind);
        Assert.IsType<AnswerOutcome>(ok);
        Assert.Contains("value=\"12\"", Assert.Single(_executor.Executed));
    }

    /// <summary>
    /// Verifies that like text containing SQL wildcard characters is rejected.
    /// </summary>
    [Theory]
    [InlineData("a_b")]
    [InlineData("a[b")]
    [InlineData("a]b")]
    public async Task LikeText_WithSqlWildcardCharacters_IsRejected(string value)
    {
        var (id, _) = await Seed(Base);

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f2", "set", value));

        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that an unpaired surrogate is rejected instead of crashing.
    /// </summary>
    [Fact]
    public async Task AnUnpairedSurrogate_IsRejected_InsteadOfCrashing()
    {
        var (id, _) = await Seed(Base);

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f2", "set", "abc\uD800def"));

        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that removing from an or group is refused because it would narrow the results.
    /// </summary>
    [Fact]
    public async Task RemovingFromAnOrGroup_IsRefused_BecauseItWouldNarrowTheResults()
    {
        var (id, _) = await Seed("<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><filter type=\"or\">" +
            "<condition attribute=\"address1_city\" operator=\"eq\" value=\"A\" /><condition attribute=\"address1_city\" operator=\"eq\" value=\"B\" /></filter></entity></fetch>");

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f1", "remove", null));

        Assert.Equal("edit_not_allowed", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that only filters emptied by the removal are cleaned up, including nested ones.
    /// </summary>
    [Fact]
    public async Task OnlyTheFiltersEmptiedByTheRemoval_AreCleanedUp_NestedOnesToo()
    {
        var (id, _) = await Seed("<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"A\" />" +
            "<filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter></filter></entity></fetch>");

        await _service.EditAsync(id, _me, new ExplainEdit("f2", "remove", null));

        var ran = Assert.Single(_executor.Executed);
        Assert.Contains("address1_city", ran);
        Assert.DoesNotContain("statecode", ran);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ran, "<filter"));
    }

    /// <summary>
    /// Verifies that distinct and the row ID alias survive an edit.
    /// </summary>
    [Fact]
    public async Task DistinctAndTheRowIdAliasSurvive_AnEdit()
    {
        var (id, _) = await Seed("<fetch top=\"500\" distinct=\"true\"><entity name=\"opportunity\"><attribute name=\"name\" /><attribute name=\"opportunityid\" alias=\"_rowid\" />" +
            "<filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter></entity></fetch>");

        await _service.EditAsync(id, _me, new ExplainEdit("f1", "remove", null));

        var ran = Assert.Single(_executor.Executed);
        Assert.Contains("distinct=\"true\"", ran);
        Assert.Contains("alias=\"_rowid\"", ran);
    }

    /// <summary>
    /// Verifies that editing an edited result keeps a single edited suffix.
    /// </summary>
    [Fact]
    public async Task EditingAnEditedResult_KeepsASingleEditedSuffix()
    {
        var (id, _) = await Seed(Base);

        var first = (AnswerOutcome)(await _service.EditAsync(id, _me, new ExplainEdit("f1", "remove", null)))!;
        var second = (AnswerOutcome)(await _service.EditAsync(first.ResultId!, _me, new ExplainEdit("f1", "remove", null)))!;

        var saved = await _store.GetAsync(_me, second.ResultId!);
        Assert.StartsWith("open deals in Istanbul (edited:", saved!.Question);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(saved.Question, "edited:"));
    }

    /// <summary>
    /// Verifies that unknown filter IDs and unknown actions are rejected.
    /// </summary>
    [Fact]
    public async Task UnknownFilterIds_AndUnknownActions_AreRejected()
    {
        var (id, _) = await Seed(Base);

        var unknown = await _service.EditAsync(id, _me, new ExplainEdit("f99", "remove", null));
        var oldStyle = await _service.EditAsync(id, _me, new ExplainEdit("x1", "remove", null));
        var action = await _service.EditAsync(id, _me, new ExplainEdit("f1", "explode", null));

        Assert.Equal("edit_unknown_filter", Assert.IsType<FailureOutcome>(unknown).Kind);
        Assert.Equal("edit_unknown_filter", Assert.IsType<FailureOutcome>(oldStyle).Kind);
        Assert.Equal("edit_invalid", Assert.IsType<FailureOutcome>(action).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that another user's or unknown result returns null without any hint.
    /// </summary>
    [Fact]
    public async Task AnotherUsersOrUnknownResult_IsNull_WithoutAnyHint()
    {
        var (id, _) = await Seed(Base);

        var other = await _service.EditAsync(id, Guid.NewGuid(), new ExplainEdit("f1", "remove", null));
        var missing = await _service.EditAsync("does-not-exist", _me, new ExplainEdit("f1", "remove", null));

        Assert.Null(other);
        Assert.Null(missing);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that an aggregate result cannot be edited.
    /// </summary>
    [Fact]
    public async Task AnAggregateResult_CannotBeEdited()
    {
        var (id, _) = await Seed("<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"statecode\" groupby=\"true\" alias=\"s\" />" +
            "<attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" /><filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter></entity></fetch>");

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f1", "remove", null));

        Assert.Equal("edit_not_allowed", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that a chain result with a fixed ID list cannot be edited.
    /// </summary>
    [Fact]
    public async Task AChainResultWithAFixedIdList_CannotBeEdited()
    {
        var ids = string.Concat(Enumerable.Range(0, 3).Select(_ => $"<value>{Guid.NewGuid()}</value>"));
        var (id, _) = await Seed($"<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><filter><condition attribute=\"customerid\" operator=\"in\">{ids}</condition>" +
            "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter></entity></fetch>");

        var outcome = await _service.EditAsync(id, _me, new ExplainEdit("f2", "remove", null));

        Assert.Equal("edit_not_allowed", Assert.IsType<FailureOutcome>(outcome).Kind);
        Assert.Empty(_executor.Executed);
    }

    /// <summary>
    /// Verifies that a different user's edit never touches the original result.
    /// </summary>
    [Fact]
    public async Task ADifferentUsersEditNeverTouchesTheOriginalResult()
    {
        var (id, _) = await Seed(Base);
        var before = (await _store.GetAsync(_me, id))!.FetchXml;

        await _service.EditAsync(id, _me, new ExplainEdit("f1", "remove", null));

        Assert.Equal(before, (await _store.GetAsync(_me, id))!.FetchXml);
    }
}
