using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.CrmViews.Models;
using CrmNlQuery.Application.CrmViews.Services;
using CrmNlQuery.Application.Summary.Models;
using CrmNlQuery.Application.Summary.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.Summary;

/// <summary>
/// Tests customer summary v2: two-hop sections and per-section open-in-CRM.
/// </summary>
public class SummaryV2Tests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AttributeInfo A(string name, string type = "String", string? label = null) => new AttributeInfo(name, label ?? name, type, new List<OptionInfo>());

    private static RelationshipInfo Rel(string from, string attribute, string to, string toId) => new RelationshipInfo("rel_" + from + "_" + attribute, from, attribute, to, toId);

    private static EntityInfo Account() => new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name"), A("createdon", "DateTime") }, new List<RelationshipInfo>());

    private static EntityInfo Opportunity() => new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name"), A("customerid", "Lookup"), A("statecode"), A("estimatedvalue", "Money"), A("estimatedclosedate", "DateTime"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { Rel("opportunity", "customerid", "account", "accountid") });

    private static EntityInfo Vehicle() => new EntityInfo("new_arac", "new_aracs", "Vehicle", "new_aracid", "new_plaka",
        new List<AttributeInfo> { A("new_aracid", "Uniqueidentifier"), A("new_plaka"), A("new_musteriid", "Lookup"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { Rel("new_arac", "new_musteriid", "account", "accountid") });

    private static EntityInfo Service() => new EntityInfo("new_servis_kaydi", "new_servis_kaydis", "Service record", "new_servis_kaydiid", "new_ad",
        new List<AttributeInfo> { A("new_servis_kaydiid", "Uniqueidentifier"), A("new_ad"), A("new_aracid", "Lookup"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { Rel("new_servis_kaydi", "new_aracid", "new_arac", "new_aracid") });

    private sealed class Views : IDefaultViewSource
    {
        /// <summary>
        /// Returns the default columns for an entity in the fake metadata used by the test.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "name" });
    }

    private sealed class Executor : IQueryExecutor
    {
        public readonly List<XDocument> Calls = new List<XDocument>();
        /// <summary>
        /// Executes a query against a fake executor in the test.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            var doc = XDocument.Parse(fetchXml);
            lock (Calls) { Calls.Add(doc); }
            if ((string?)doc.Root!.Attribute("aggregate") == "true")
            {
                return Task.FromResult(new ResultTable(new[] { "n" }, new List<IReadOnlyList<string>> { new[] { "3" } }));
            }
            var columns = doc.Root.Element("entity")!.Elements("attribute").Select(a => (string?)a.Attribute("alias") ?? (string)a.Attribute("name")!).ToList();
            var visible = columns.Where(c => c != "_rowid").ToList();
            return Task.FromResult(new ResultTable(visible, new List<IReadOnlyList<string>> { visible.Select(c => c + "-v").ToList() }, columns.Contains("_rowid") ? new List<string> { Guid.NewGuid().ToString() } : null));
        }
    }

    private static SummaryBuilder Builder(Executor executor, params EntityInfo[] tables)
        => new SummaryBuilder(new FakeMetadataSource(tables), executor, new Views(), clock: () => new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero));

    private static readonly Caller Alice = Caller.User(Guid.NewGuid());

    /// <summary>
    /// Verifies that a table linked only through an open related table becomes a via section with a joined count.
    /// </summary>
    [Fact]
    public async Task ATableLinkedOnlyThroughAnOpenRelatedTable_BecomesAViaSection_WithAJoinedCount()
    {
        var executor = new Executor();

        var outcome = await Builder(executor, Account(), Vehicle(), Service()).BuildAsync(Alice, "account", AccountId, "Brightwave");

        var via = outcome.Sections.Single(s => s.Entity == "new_servis_kaydi");
        Assert.Equal("generic", via.Kind);
        Assert.Equal("new_arac", via.ViaEntity);
        Assert.Equal("Vehicle", via.Via);
        Assert.Contains(via.Metrics, m => m.Label == "Service record" && m.Value == "3");
        var count = executor.Calls.Single(d => d.Root!.Element("entity")!.Attribute("name")!.Value == "new_servis_kaydi" && (string?)d.Root.Attribute("aggregate") == "true");
        var link = count.Descendants("link-entity").Single();
        Assert.Equal("new_arac", (string?)link.Attribute("name"));
        Assert.Equal("new_aracid", (string?)link.Attribute("from"));
        Assert.Equal("new_aracid", (string?)link.Attribute("to"));
        Assert.Contains(AccountId.ToString(), link.ToString());
        Assert.Contains(outcome.Sections, s => s.Entity == "new_arac" && s.ViaEntity is null);
    }

    /// <summary>
    /// Verifies that without the middle table open there is no via section and no query for it.
    /// </summary>
    [Fact]
    public async Task WithoutTheMiddleTableOpen_ThereIsNoViaSection_AndNoQueryForIt()
    {
        var executor = new Executor();

        var outcome = await Builder(executor, Account(), Service()).BuildAsync(Alice, "account", AccountId, "Brightwave");

        Assert.DoesNotContain(outcome.Sections, s => s.Entity == "new_servis_kaydi");
        Assert.DoesNotContain(executor.Calls, d => d.Root!.Element("entity")!.Attribute("name")!.Value == "new_servis_kaydi");
    }

    /// <summary>
    /// Verifies that a table that is also directly related is not listed again as a via section.
    /// </summary>
    [Fact]
    public async Task ATableThatIsAlsoDirectlyRelated_IsNotListedAgainAsAViaSection()
    {
        var both = new EntityInfo("new_servis_kaydi", "new_servis_kaydis", "Service record", "new_servis_kaydiid", "new_ad",
            new List<AttributeInfo> { A("new_servis_kaydiid", "Uniqueidentifier"), A("new_ad"), A("new_aracid", "Lookup"), A("new_musteriid", "Lookup") },
            new List<RelationshipInfo> { Rel("new_servis_kaydi", "new_aracid", "new_arac", "new_aracid"), Rel("new_servis_kaydi", "new_musteriid", "account", "accountid") });

        var outcome = await Builder(new Executor(), Account(), Vehicle(), both).BuildAsync(Alice, "account", AccountId, "x");

        Assert.Single(outcome.Sections, s => s.Entity == "new_servis_kaydi");
        Assert.All(outcome.Sections.Where(s => s.Entity == "new_servis_kaydi"), s => Assert.Null(s.ViaEntity));
    }

    /// <summary>
    /// Verifies that the list query is the full sorted section query without top and only for real sections.
    /// </summary>
    [Fact]
    public async Task TheListQuery_IsTheFullSortedSectionQuery_WithoutTop_AndOnlyForRealSections()
    {
        var builder = Builder(new Executor(), Account(), Opportunity(), Vehicle(), Service());

        var opportunities = await builder.ListFetchXmlAsync("account", AccountId, "opportunities", "opportunity", null);
        var via = await builder.ListFetchXmlAsync("account", AccountId, "generic", "new_servis_kaydi", "new_arac");

        var doc = XDocument.Parse(opportunities!);
        Assert.Null(doc.Root!.Attribute("top"));
        Assert.Contains(doc.Descendants("condition"), c => (string?)c.Attribute("attribute") == "statecode" && (string?)c.Attribute("value") == "0");
        Assert.Contains(doc.Descendants("condition"), c => (string?)c.Attribute("attribute") == "customerid" && (string?)c.Attribute("value") == AccountId.ToString());
        Assert.Equal("estimatedvalue", (string?)doc.Descendants("order").Single().Attribute("attribute"));
        Assert.Contains("link-entity", via!);
        Assert.Null(await builder.ListFetchXmlAsync("account", AccountId, "generic", "new_servis_kaydi", null));
        Assert.Null(await builder.ListFetchXmlAsync("account", AccountId, "basic", "account", null));
        Assert.Null(await builder.ListFetchXmlAsync("account", AccountId, "opportunities", "new_model", null));
    }

    private readonly MemoryHistoryStore _history = new MemoryHistoryStore();
    private readonly MemoryUserViewStore _views = new MemoryUserViewStore();
    private bool _enabled = true;

    private OpenInCrmService OpenService()
    {
        var metadata = new FakeMetadataSource(Account(), Opportunity(), Vehicle(), Service());
        var summary = new SummaryBuilder(metadata, new Executor(), new Views());
        return new OpenInCrmService(_history, new ViewBuilder(new PlanValidator(metadata), metadata), _views, () => _enabled, "http://crm.example/org/", summary);
    }

    private async Task<string> StoredSummaryAsync(Guid owner)
    {
        var fetch = $"<fetch top=\"1\"><entity name=\"account\"><attribute name=\"accountid\" /><filter><condition attribute=\"accountid\" operator=\"eq\" value=\"{AccountId:N}\" /></filter></entity></fetch>";
        return await _history.AddAsync(owner, "Summarize Brightwave", "account", fetch, kind: "summary");
    }

    /// <summary>
    /// Verifies that opening a section writes its list as the personal view for the owner of the summary.
    /// </summary>
    [Fact]
    public async Task OpeningASection_WritesItsListAsThePersonalView_ForTheOwnerOfTheSummary()
    {
        var me = Guid.NewGuid();
        var id = await StoredSummaryAsync(me);

        var result = await OpenService().OpenSectionAsync(me, id, "opportunities", "opportunity", null);

        Assert.Equal(OpenInCrmStatus.Opened, result.Status);
        var view = Assert.Single(_views.All);
        Assert.Equal("opportunity", view.Definition.Entity);
        Assert.Null(XDocument.Parse(view.Definition.FetchXml).Root!.Attribute("top"));
        Assert.Equal(me, view.Owner);
        Assert.Equal(OpenInCrmService.ViewName, view.Name);
    }

    /// <summary>
    /// Verifies that a via section can also be opened as a view when the view builder accepts the join.
    /// </summary>
    [Fact]
    public async Task AViaSection_CanBeOpenedAsAView_TooWhenTheViewBuilderAcceptsTheJoin()
    {
        var me = Guid.NewGuid();
        var id = await StoredSummaryAsync(me);

        var result = await OpenService().OpenSectionAsync(me, id, "generic", "new_servis_kaydi", "new_arac");

        Assert.Equal(OpenInCrmStatus.Opened, result.Status);
        Assert.Contains("link-entity", Assert.Single(_views.All).Definition.FetchXml);
    }

    /// <summary>
    /// Verifies that a section cannot be opened from another user's summary, a flat result, or when the flag is off.
    /// </summary>
    [Fact]
    public async Task ASectionCannotBeOpened_FromAnotherUsersSummary_AFlatResult_OrWhenTheFlagIsOff()
    {
        var me = Guid.NewGuid();
        var id = await StoredSummaryAsync(me);
        var flat = await _history.AddAsync(me, "q", "account", "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>");

        Assert.Equal(OpenInCrmStatus.NotFound, (await OpenService().OpenSectionAsync(Guid.NewGuid(), id, "opportunities", "opportunity", null)).Status);
        Assert.Equal(OpenInCrmStatus.NotFound, (await OpenService().OpenSectionAsync(me, flat, "opportunities", "opportunity", null)).Status);
        Assert.Equal(OpenInCrmStatus.NotFound, (await OpenService().OpenSectionAsync(me, id, "opportunities", "new_model", null)).Status);
        _enabled = false;
        Assert.Equal(OpenInCrmStatus.Disabled, (await OpenService().OpenSectionAsync(me, id, "opportunities", "opportunity", null)).Status);
        Assert.Empty(_views.All);
    }

    /// <summary>
    /// Verifies that a via section never writes an empty filter in its count or list.
    /// </summary>
    [Fact]
    public async Task AViaSection_NeverWritesAnEmptyFilter_InItsCountOrItsList()
    {
        var executor = new Executor();
        var builder = Builder(executor, Account(), Vehicle(), Service());

        await builder.BuildAsync(Alice, "account", AccountId, "x");
        var list = await builder.ListFetchXmlAsync("account", AccountId, "generic", "new_servis_kaydi", "new_arac");

        Assert.DoesNotContain(executor.Calls.Where(d => d.Root!.Element("entity")!.Attribute("name")!.Value == "new_servis_kaydi"), d => d.Root!.Element("entity")!.Elements("filter").Any(f => !f.HasElements));
        Assert.DoesNotContain("<filter />", list);
    }

    /// <summary>
    /// Verifies that the list query leaves out closed optional fields so no closed field reaches the view.
    /// </summary>
    [Fact]
    public async Task TheListQuery_LeavesOutClosedOptionalFields_SoNoClosedFieldReachesTheView()
    {
        var opp = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
            new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name"), A("customerid", "Lookup"), A("statecode") },
            new List<RelationshipInfo> { Rel("opportunity", "customerid", "account", "accountid") });
        var builder = Builder(new Executor(), Account(), opp);

        var list = await builder.ListFetchXmlAsync("account", AccountId, "opportunities", "opportunity", null);

        var names = XDocument.Parse(list!).Descendants().Where(e => e.Name == "attribute" || e.Name == "order").Select(e => (string?)e.Attribute("name") ?? (string?)e.Attribute("attribute")).ToList();
        Assert.DoesNotContain("estimatedvalue", names);
        Assert.DoesNotContain("estimatedclosedate", names);
        Assert.DoesNotContain("createdon", names);
        Assert.Contains("name", names);
    }

    /// <summary>
    /// Verifies that the contract list for the view carries the ninety-day window only in the summary query, not the full list.
    /// </summary>
    [Fact]
    public async Task TheContractListForTheView_CarriesTheNinetyDayWindowOnlyInTheSummaryQuery_NotInTheFullList()
    {
        var contract = new EntityInfo("contract", "contracts", "Contract", "contractid", "title",
            new List<AttributeInfo> { A("contractid", "Uniqueidentifier"), A("title"), A("customerid", "Lookup"), A("statecode"), A("expireson", "DateTime") },
            new List<RelationshipInfo> { Rel("contract", "customerid", "account", "accountid") });
        var executor = new Executor();
        var builder = Builder(executor, Account(), contract);

        await builder.BuildAsync(Alice, "account", AccountId, "x");
        var full = XDocument.Parse((await builder.ListFetchXmlAsync("account", AccountId, "contracts", "contract", null))!);

        Assert.DoesNotContain(full.Descendants("condition"), c => (string?)c.Attribute("operator") == "on-or-before");
        Assert.Contains(executor.Calls, d => d.Root!.Element("entity")!.Attribute("name")!.Value == "contract" && d.Descendants("condition").Any(c => (string?)c.Attribute("operator") == "on-or-before") && d.Root.Attribute("top") is not null);
    }

    /// <summary>
    /// Verifies that offering sections uses one discovery pass and marks only the openable ones.
    /// </summary>
    [Fact]
    public async Task OfferingTheSections_UsesOneDiscoveryPass_AndMarksOnlyTheOpenableOnes()
    {
        var sections = new List<SummarySection>
        {
            new SummarySection("basic", "account", "Account", SummaryStatus.Ok, Array.Empty<SummaryMetric>(), Array.Empty<SummaryItem>()),
            new SummarySection("opportunities", "opportunity", "Opportunity", SummaryStatus.Ok, Array.Empty<SummaryMetric>(), Array.Empty<SummaryItem>()),
            new SummarySection("opportunities", "opportunity", "Opportunity", SummaryStatus.Unavailable, Array.Empty<SummaryMetric>(), Array.Empty<SummaryItem>()) { },
        };

        var offered = await OpenService().OfferSectionsAsync("account", AccountId, sections);

        Assert.False(offered[0].CanOpen);
        Assert.True(offered[1].CanOpen);
        Assert.False(offered[2].CanOpen);
        _enabled = false;
        Assert.All(await OpenService().OfferSectionsAsync("account", AccountId, sections), s => Assert.False(s.CanOpen));
    }

    /// <summary>
    /// Verifies that the section button is offered only when the flag is on for real non empty sections.
    /// </summary>
    [Fact]
    public async Task TheSectionButton_IsOfferedOnlyWhenTheFlagIsOn_ForRealNonEmptySections()
    {
        var ok = new SummarySection("opportunities", "opportunity", "Opportunity", SummaryStatus.Ok, Array.Empty<SummaryMetric>(), Array.Empty<SummaryItem>());
        var basic = ok with { Kind = "basic", Entity = "account" };
        var unavailable = ok with { Status = SummaryStatus.Unavailable };
        var service = OpenService();

        Assert.True(await service.CanOfferSectionAsync("account", AccountId, ok));
        Assert.False(await service.CanOfferSectionAsync("account", AccountId, basic));
        Assert.False(await service.CanOfferSectionAsync("account", AccountId, unavailable));
        _enabled = false;
        Assert.False(await service.CanOfferSectionAsync("account", AccountId, ok));
    }
}
