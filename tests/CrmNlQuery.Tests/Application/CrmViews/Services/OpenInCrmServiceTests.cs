using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.CrmViews.Abstractions;
using CrmNlQuery.Application.CrmViews.Models;
using CrmNlQuery.Application.CrmViews.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.CrmViews.Services;

/// <summary>
/// Tests the Open in CRM rules: flag, ownership, eligibility, permission fallback and deletion, without calling the CRM.
/// </summary>
public class OpenInCrmServiceTests
{
    private const string Fetch = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /><order attribute=\"name\" /></entity></fetch>";
    private const string CrmBase = "http://crm.example/org/";

    private sealed class ThrowingViews : IUserViewStore
    {
        public CrmException? Error { get; set; }
        public int Calls { get; private set; }
        /// <summary>
        /// Throws to simulate the CRM refusing to write a view.
        /// </summary>
        public Task<string> UpsertAsync(Guid caller, string name, ViewDefinition view, CancellationToken ct = default)
        {
            Calls++;
            throw Error ?? new CrmException("boom");
        }
        /// <summary>
        /// Throws to simulate the CRM failing to delete a view.
        /// </summary>
        public Task DeleteAsync(Guid caller, string name, CancellationToken ct = default)
        {
            Calls++;
            throw Error ?? new CrmException("boom");
        }
    }

    private readonly MemoryHistoryStore _history = new MemoryHistoryStore();
    private readonly MemoryUserViewStore _views = new MemoryUserViewStore();
    private bool _enabled = true;

    private OpenInCrmService Service(IUserViewStore? views = null)
    {
        var metadata = TestMetadata.Create();
        return new OpenInCrmService(_history, new ViewBuilder(new PlanValidator(metadata), metadata), views ?? _views, () => _enabled, CrmBase);
    }

    private static AnswerOutcome Answer(string fetch, bool truncated, string? resultId = "abc")
        => new AnswerOutcome("account", "Showing Account", fetch, new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>()), truncated) { ResultId = resultId };

    /// <summary>
    /// Verifies that the button is offered, only for truncated saved answers, and when the flag is on.
    /// </summary>
    [Fact]
    public async Task TheButton_IsOffered_OnlyForTruncatedSavedAnswers_WhenTheFlagIsOn()
    {
        Assert.True(await Service().CanOfferAsync(Answer(Fetch, truncated: true)));
        Assert.False(await Service().CanOfferAsync(Answer(Fetch, truncated: false)));
        Assert.False(await Service().CanOfferAsync(Answer(Fetch, truncated: true, resultId: null)));
        _enabled = false;
        Assert.False(await Service().CanOfferAsync(Answer(Fetch, truncated: true)));
    }

    /// <summary>
    /// Verifies that the button is never offered for an aggregate.
    /// </summary>
    [Fact]
    public async Task TheButton_IsNeverOffered_ForAnAggregate()
    {
        var aggregate = "<fetch aggregate=\"true\"><entity name=\"account\"><attribute name=\"accountid\" alias=\"n\" aggregate=\"count\" /></entity></fetch>";

        Assert.False(await Service().CanOfferAsync(Answer(aggregate, truncated: true)));
    }

    /// <summary>
    /// Verifies that the button is never offered for a one to many column.
    /// </summary>
    [Fact]
    public async Task TheButton_IsNeverOffered_ForAOneToManyColumn()
    {
        var oneToMany = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" />" +
            "<link-entity name=\"contract\" from=\"customerid\" to=\"accountid\" alias=\"c\"><attribute name=\"title\" /></link-entity></entity></fetch>";

        Assert.False(await Service().CanOfferAsync(Answer(oneToMany, truncated: true)));
    }

    /// <summary>
    /// Verifies that a saved result is written as one named personal view and the URL points at it.
    /// </summary>
    [Fact]
    public async Task ASavedResult_IsWrittenAsOneNamedPersonalView_AndTheUrlPointsAtIt()
    {
        var me = Guid.NewGuid();
        var id = await _history.AddAsync(me, "tüm hesaplar", "account", Fetch);

        var result = await Service().OpenAsync(me, id);

        Assert.Equal(OpenInCrmStatus.Opened, result.Status);
        var view = Assert.Single(_views.All);
        Assert.Equal(me, view.Owner);
        Assert.Equal(OpenInCrmService.ViewName, view.Name);
        Assert.Equal("account", view.Definition.Entity);
        Assert.DoesNotContain("tüm hesaplar", view.Definition.FetchXml);
        Assert.Null(System.Xml.Linq.XDocument.Parse(view.Definition.FetchXml).Root!.Attribute("top"));
        Assert.StartsWith("http://crm.example/org/main.aspx?etn=account&pagetype=entitylist&viewid=%7b" + view.Id.ToString("D") + "%7d", result.Url);
        Assert.EndsWith("&viewtype=4230", result.Url);
    }

    /// <summary>
    /// Verifies that opening twice updates the same single view.
    /// </summary>
    [Fact]
    public async Task OpeningTwice_UpdatesTheSameSingleView()
    {
        var me = Guid.NewGuid();
        var first = await _history.AddAsync(me, "q1", "account", Fetch);
        var second = await _history.AddAsync(me, "q2", "account", Fetch.Replace("name\" /><order", "name\" /><attribute name=\"address1_city\" /><order"));

        await Service().OpenAsync(me, first);
        var again = await Service().OpenAsync(me, second);

        var view = Assert.Single(_views.All);
        Assert.Contains(view.Id.ToString("D"), again.Url);
        Assert.Equal(new[] { "name", "address1_city" }, view.Definition.Columns.ToArray());
    }

    /// <summary>
    /// Verifies that each user gets their own view.
    /// </summary>
    [Fact]
    public async Task EachUser_GetsTheirOwnView()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var ra = await _history.AddAsync(a, "q", "account", Fetch);
        var rb = await _history.AddAsync(b, "q", "account", Fetch);

        await Service().OpenAsync(a, ra);
        await Service().OpenAsync(b, rb);

        Assert.Equal(2, _views.All.Count);
        Assert.Equal(2, _views.All.Select(v => v.Owner).Distinct().Count());
    }

    /// <summary>
    /// Verifies that a shared view with the same name from someone else is never touched.
    /// </summary>
    [Fact]
    public async Task ASharedViewWithTheSameName_FromSomeoneElse_IsNeverTouched()
    {
        var me = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var foreign = _views.AddForeignView(stranger, OpenInCrmService.ViewName, new ViewDefinition("account", "<fetch />", new List<string> { "name" }, "accountid", "name"));
        var id = await _history.AddAsync(me, "q", "account", Fetch);

        await Service().OpenAsync(me, id);
        await Service().DeleteViewAsync(me);

        var left = Assert.Single(_views.All);
        Assert.Equal(foreign, left.Id);
        Assert.Equal(stranger, left.Owner);
    }

    /// <summary>
    /// Verifies that when the flag is off nothing is read or written.
    /// </summary>
    [Fact]
    public async Task WhenTheFlagIsOff_NothingIsRead_OrWritten()
    {
        var me = Guid.NewGuid();
        var id = await _history.AddAsync(me, "q", "account", Fetch);
        _enabled = false;

        var result = await Service().OpenAsync(me, id);

        Assert.Equal(OpenInCrmStatus.Disabled, result.Status);
        Assert.Empty(_views.All);
    }

    /// <summary>
    /// Verifies that a someone elses or unknown result is not found without writing.
    /// </summary>
    [Fact]
    public async Task ASomeoneElsesOrUnknownResult_IsNotFound_WithoutWriting()
    {
        var owner = Guid.NewGuid();
        var id = await _history.AddAsync(owner, "secret", "account", Fetch);

        Assert.Equal(OpenInCrmStatus.NotFound, (await Service().OpenAsync(Guid.NewGuid(), id)).Status);
        Assert.Equal(OpenInCrmStatus.NotFound, (await Service().OpenAsync(owner, "nope")).Status);
        Assert.Equal(OpenInCrmStatus.NotFound, (await Service().OpenAsync(owner, Guid.NewGuid().ToString("N"))).Status);
        Assert.Empty(_views.All);
    }

    /// <summary>
    /// Verifies that a stored query the validator no longer accepts is never written.
    /// </summary>
    [Fact]
    public async Task AStoredQueryTheValidatorNoLongerAccepts_IsNeverWritten()
    {
        var me = Guid.NewGuid();
        var id = await _history.AddAsync(me, "q", "account", "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"column_removed_from_crm\" /></entity></fetch>");

        var result = await Service().OpenAsync(me, id);

        Assert.Equal(OpenInCrmStatus.NotEligible, result.Status);
        Assert.Equal("Invalid", result.Message);
        Assert.Empty(_views.All);
    }

    /// <summary>
    /// Verifies that without the privilege the user is told and the fallback stays.
    /// </summary>
    [Fact]
    public async Task WithoutThePrivilege_TheUserIsToldAndTheFallbackStays()
    {
        var me = Guid.NewGuid();
        var id = await _history.AddAsync(me, "q", "account", Fetch);
        var views = new ThrowingViews { Error = new CrmException("missing prvCreateUserQuery privilege", 403, isPermissionDenied: true) };

        var result = await Service(views).OpenAsync(me, id);

        Assert.Equal(OpenInCrmStatus.PermissionDenied, result.Status);
        Assert.Null(result.Url);
        Assert.Contains("permission", result.Message);
        Assert.DoesNotContain("prvCreateUserQuery", result.Message);
    }

    /// <summary>
    /// Verifies that any other CRM error is a generic failure without leaking details.
    /// </summary>
    [Fact]
    public async Task AnyOtherCrmError_IsAGenericFailure_WithoutLeakingDetails()
    {
        var me = Guid.NewGuid();
        var id = await _history.AddAsync(me, "q", "account", Fetch);
        var views = new ThrowingViews { Error = new CrmException("HTTP 500 at http://internal/org/api guid 1234") };

        var result = await Service(views).OpenAsync(me, id);

        Assert.Equal(OpenInCrmStatus.Failed, result.Status);
        Assert.DoesNotContain("internal", result.Message);
    }

    /// <summary>
    /// Verifies that deleting the view never blocks the history deletion when the CRM fails.
    /// </summary>
    [Fact]
    public async Task DeletingTheView_NeverBlocksTheHistoryDeletion_WhenTheCrmFails()
    {
        var views = new ThrowingViews();

        await Service(views).DeleteViewAsync(Guid.NewGuid());

        Assert.Equal(1, views.Calls);
    }

    /// <summary>
    /// Verifies that deleting the view removes only the callers named view.
    /// </summary>
    [Fact]
    public async Task DeletingTheView_RemovesOnlyTheCallersNamedView()
    {
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var mine = await _history.AddAsync(me, "q", "account", Fetch);
        var theirs = await _history.AddAsync(other, "q", "account", Fetch);
        await Service().OpenAsync(me, mine);
        await Service().OpenAsync(other, theirs);

        await Service().DeleteViewAsync(me);

        Assert.Equal(other, Assert.Single(_views.All).Owner);
    }

    private sealed class BrokenMetadata : CrmNlQuery.Application.Ask.Abstractions.IMetadataSource
    {
        /// <summary>
        /// Test metadata source that always fails, lists no entities, used to check that Open in CRM copes with broken schema metadata.
        /// </summary>
        public Task<IReadOnlyList<CrmNlQuery.Domain.Metadata.Entities.EntitySummary>> ListEntitiesAsync(CancellationToken ct = default) => throw new CrmException("down");
        /// <summary>
        /// Test metadata source that always fails, returns no entity info, used to check that Open in CRM copes with broken schema metadata.
        /// </summary>
        public Task<CrmNlQuery.Domain.Metadata.Entities.EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) => throw new CrmException("down");
        /// <summary>
        /// Does nothing in this broken test metadata source, since refreshing is irrelevant to the failure being simulated.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// Verifies that when the metadata can not be read the button is simply not offered and the answer is not broken.
    /// </summary>
    [Fact]
    public async Task WhenTheMetadataCanNotBeRead_TheButtonIsSimplyNotOffered_AndTheAnswerIsNotBroken()
    {
        var broken = new BrokenMetadata();
        var service = new OpenInCrmService(_history, new ViewBuilder(new PlanValidator(broken), broken), _views, () => true, CrmBase);

        Assert.False(await service.CanOfferAsync(Answer(Fetch, truncated: true)));
    }
}
