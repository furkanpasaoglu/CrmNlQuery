using System.Xml.Linq;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Summary.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Summary.Services;

/// <summary>
/// Tests the customer summary builder: sections by existing tables, independent sections, caller-scoped queries and custom tables as generic sections.
/// </summary>
public class SummaryBuilderTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AttributeInfo A(string name, string type = "String", string? label = null) => new AttributeInfo(name, label ?? name, type, new List<OptionInfo>());

    private static RelationshipInfo ToAccount(string table, string attribute) => new RelationshipInfo("rel_" + attribute, table, attribute, "account", "accountid");

    private static EntityInfo Account() => new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name"), A("ownerid", "Lookup"), A("createdon", "DateTime"), A("address1_city", "String", "City"), A("telephone1", "String", "Main Phone") },
        new List<RelationshipInfo>());

    private static EntityInfo Opportunity() => new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name"), A("customerid", "Lookup"), A("statecode"), A("estimatedvalue", "Money"), A("estimatedclosedate", "DateTime"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { ToAccount("opportunity", "customerid") });

    private static EntityInfo Activity() => new EntityInfo("activitypointer", "activitypointers", "Activity", "activityid", "subject",
        new List<AttributeInfo> { A("activityid", "Uniqueidentifier"), A("subject"), A("regardingobjectid", "Lookup"), A("activitytypecode"), A("actualend", "DateTime"), A("scheduledend", "DateTime"), A("statecode"), A("modifiedon", "DateTime"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { ToAccount("activitypointer", "regardingobjectid") });

    private static EntityInfo Incident() => new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
        new List<AttributeInfo> { A("incidentid", "Uniqueidentifier"), A("title"), A("customerid", "Lookup"), A("statecode"), A("prioritycode", "Picklist"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { ToAccount("incident", "customerid") });

    private static EntityInfo Contract() => new EntityInfo("contract", "contracts", "Contract", "contractid", "title",
        new List<AttributeInfo> { A("contractid", "Uniqueidentifier"), A("title"), A("customerid", "Lookup"), A("statecode"), A("expireson", "DateTime") },
        new List<RelationshipInfo> { ToAccount("contract", "customerid") });

    private static EntityInfo Order() => new EntityInfo("salesorder", "salesorders", "Order", "salesorderid", "name",
        new List<AttributeInfo> { A("salesorderid", "Uniqueidentifier"), A("name"), A("customerid", "Lookup"), A("statecode") },
        new List<RelationshipInfo> { ToAccount("salesorder", "customerid") });

    private static EntityInfo Vehicle() => new EntityInfo("new_arac", "new_aracs", "Vehicle", "new_aracid", "new_plaka",
        new List<AttributeInfo> { A("new_aracid", "Uniqueidentifier"), A("new_plaka"), A("new_musteriid", "Lookup"), A("new_ikinciid", "Lookup"), A("createdon", "DateTime") },
        new List<RelationshipInfo> { ToAccount("new_arac", "new_musteriid"), ToAccount("new_arac", "new_ikinciid") });

    private static EntityInfo Unrelated() => new EntityInfo("new_model", "new_models", "Model", "new_modelid", "new_ad",
        new List<AttributeInfo> { A("new_modelid", "Uniqueidentifier"), A("new_ad") }, new List<RelationshipInfo>());

    private sealed class FakeViews : IDefaultViewSource
    {
        /// <summary>
        /// Test fake that returns default view columns for an entity.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "name", "address1_city", "telephone1", "accountid" });
    }

    private sealed class FakeExecutor : IQueryExecutor
    {
        public readonly List<(string Set, Caller Caller, XDocument Doc)> Calls = new List<(string, Caller, XDocument)>();
        public Func<string, XDocument, ResultTable?>? Override;
        public Exception? Throw;
        public string? ThrowFor;

        /// <summary>
        /// Test fake executor that returns canned results for summary queries.
        /// </summary>
        public async Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            var doc = XDocument.Parse(fetchXml);
            lock (Calls) { Calls.Add((entitySetName, caller, doc)); }
            if (Throw is not null && (ThrowFor is null || ThrowFor == entitySetName)) { throw Throw; }
            var custom = Override?.Invoke(entitySetName, doc);
            if (custom is not null) { return custom; }
            await Task.Yield();
            var fetch = doc.Root!;
            if ((string?)fetch.Attribute("aggregate") == "true")
            {
                var hasSum = fetch.Descendants("attribute").Any(a => (string?)a.Attribute("alias") == "s");
                var grouped = fetch.Descendants("attribute").Any(a => (string?)a.Attribute("groupby") == "true");
                if (grouped) { return new ResultTable(new[] { "p", "n" }, new List<IReadOnlyList<string>> { new[] { "High", "2" }, new[] { "Normal", "1" } }); }
                return hasSum
                    ? new ResultTable(new[] { "n", "s" }, new List<IReadOnlyList<string>> { new[] { "3", "166000" } })
                    : new ResultTable(new[] { "n" }, new List<IReadOnlyList<string>> { new[] { "2" } });
            }
            var entity = fetch.Element("entity")!;
            var columns = entity.Elements("attribute").Select(a => (string?)a.Attribute("alias") ?? (string)a.Attribute("name")!).ToList();
            var visible = columns.Where(c => c != "_rowid").ToList();
            var row = visible.Select(c => c + "-value").ToList();
            var ids = columns.Contains("_rowid") ? new List<string> { Guid.NewGuid().ToString() } : null;
            return new ResultTable(visible, new List<IReadOnlyList<string>> { row }, ids);
        }
    }

    private static readonly Caller Alice = Caller.User(Guid.NewGuid());

    private static SummaryBuilder Builder(FakeExecutor executor, TimeSpan? timeout = null, params EntityInfo[] tables)
        => new SummaryBuilder(new FakeMetadataSource(tables), executor, new FakeViews(), () => timeout ?? TimeSpan.FromSeconds(5), () => new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero));

    private static EntityInfo[] Everything() => new[] { Account(), Opportunity(), Activity(), Incident(), Contract(), Order(), Vehicle(), Unrelated() };

    private static Task<SummaryOutcome> BuildAsync(FakeExecutor executor, params EntityInfo[] tables)
        => Builder(executor, null, tables).BuildAsync(Alice, "account", AccountId, "Brightwave");

    /// <summary>
    /// Verifies that summary intent detection recognizes summary requests and ignores other questions.
    /// </summary>
    [Theory]
    [InlineData("Örnekova'yı özetle", true)]
    [InlineData("Brightwave'in özetini ver", true)]
    [InlineData("Summarize Brightwave Appliances", true)]
    [InlineData("give me an overview of Brightwave", true)]
    [InlineData("brief me on Acme", true)]
    [InlineData("OZETLE", true)]
    [InlineData("Show accounts in London", false)]
    [InlineData("Londra'daki firmalar", false)]
    [InlineData("assumption of the sums", false)]
    [InlineData("", false)]
    public void TheIntentRule_RecognisesSummaryRequests_InTurkishAndEnglish(string question, bool expected)
    {
        Assert.Equal(expected, SummaryIntent.Matches(question));
    }

    /// <summary>
    /// Verifies that every section kind is built in a fixed order when the tables exist.
    /// </summary>
    [Fact]
    public async Task EverySectionKind_IsBuilt_InAFixedOrder_WhenTheTablesExist()
    {
        var outcome = await BuildAsync(new FakeExecutor(), Everything());

        Assert.Equal(new[] { "basic", "opportunities", "activities", "cases", "contracts", "orders", "generic" }, outcome.Sections.Select(s => s.Kind));
        var opportunities = outcome.Sections.Single(s => s.Kind == "opportunities");
        Assert.Equal("3", opportunities.Metrics.Single(m => m.Key == "count").Value);
        Assert.Equal("166000", opportunities.Metrics.Single(m => m.Key == "total").Value);
        Assert.Equal("name-value", opportunities.Items[0].Text);
        Assert.Equal("opportunity", opportunities.Items[0].Entity);
        Assert.NotNull(opportunities.Items[0].Id);
        var cases = outcome.Sections.Single(s => s.Kind == "cases");
        Assert.Contains(cases.Metrics, m => m.Key == "priority:High" && m.Value == "2");
        Assert.Equal("Brightwave", outcome.RecordName);
        Assert.Equal("account", outcome.Entity);
    }

    /// <summary>
    /// Verifies that a missing or closed table has no section and an unrelated table never does.
    /// </summary>
    [Fact]
    public async Task ATableThatIsMissingOrClosed_HasNoSection_AndAnUnrelatedTableNever()
    {
        var outcome = await BuildAsync(new FakeExecutor(), Account(), Opportunity(), Unrelated());

        Assert.Equal(new[] { "basic", "opportunities" }, outcome.Sections.Select(s => s.Kind));
    }

    /// <summary>
    /// Verifies that a custom table becomes a generic section with count and newest records using every lookup to the record.
    /// </summary>
    [Fact]
    public async Task ACustomTable_BecomesAGenericSection_WithCountAndNewestRecords_UsingEveryLookupToTheRecord()
    {
        var executor = new FakeExecutor();

        var outcome = await BuildAsync(executor, Account(), Vehicle());

        var generic = outcome.Sections.Single(s => s.Kind == "generic");
        Assert.Equal("Vehicle", generic.Title);
        Assert.Equal("new_arac", generic.Entity);
        Assert.Equal("2", generic.Metrics.Single(m => m.Key == "count").Value);
        var query = executor.Calls.Where(c => c.Set == "new_aracs").Select(c => c.Doc).First();
        var filter = query.Descendants("filter").First(f => f.Attribute("type") is not null);
        Assert.Equal("or", (string?)filter.Attribute("type"));
        Assert.Equal(new[] { "new_musteriid", "new_ikinciid" }, filter.Elements("condition").Select(c => (string?)c.Attribute("attribute")));
    }

    /// <summary>
    /// Verifies that an empty generic section is hidden while a known section with zero is shown.
    /// </summary>
    [Fact]
    public async Task AGenericSectionWithNoRecords_IsNotShown_ButAKnownSectionWithZeroIs()
    {
        var executor = new FakeExecutor
        {
            Override = (set, doc) => set is "new_aracs" or "contracts" && (string?)doc.Root!.Attribute("aggregate") == "true"
                ? new ResultTable(new[] { "n" }, new List<IReadOnlyList<string>> { new[] { "0" } })
                : null,
        };

        var outcome = await BuildAsync(executor, Account(), Vehicle(), Contract());

        Assert.DoesNotContain(outcome.Sections, s => s.Kind == "generic");
        Assert.Equal("0", outcome.Sections.Single(s => s.Kind == "contracts").Metrics.Single(m => m.Key == "count").Value);
    }

    /// <summary>
    /// Verifies that a failing section is isolated and the others still show.
    /// </summary>
    [Fact]
    public async Task AFailingSection_IsIsolated_OthersStillShow()
    {
        var executor = new FakeExecutor { Throw = new CrmException("boom", 500), ThrowFor = "incidents" };

        var outcome = await BuildAsync(executor, Everything());

        Assert.Equal("unavailable", outcome.Sections.Single(s => s.Kind == "cases").Status);
        Assert.Equal("ok", outcome.Sections.Single(s => s.Kind == "opportunities").Status);
        Assert.Equal(7, outcome.Sections.Count);
    }

    /// <summary>
    /// Verifies that a table the user may not read is left out entirely.
    /// </summary>
    [Fact]
    public async Task ATableTheUserMayNotRead_IsLeftOutEntirely()
    {
        var executor = new FakeExecutor { Throw = new CrmException("no", 403, isPermissionDenied: true), ThrowFor = "contracts" };

        var outcome = await BuildAsync(executor, Everything());

        Assert.DoesNotContain(outcome.Sections, s => s.Kind == "contracts");
        Assert.Contains(outcome.Sections, s => s.Kind == "orders");
    }

    /// <summary>
    /// Verifies that a slow section times out only for itself.
    /// </summary>
    [Fact]
    public async Task ASlowSection_TimesOut_OnlyThere()
    {
        var executor = new FakeExecutor
        {
            Override = (set, doc) => null,
        };
        var slow = new SlowExecutor(executor, "activitypointers");
        var builder = new SummaryBuilder(new FakeMetadataSource(Everything()), slow, new FakeViews(), () => TimeSpan.FromMilliseconds(100));

        var outcome = await builder.BuildAsync(Alice, "account", AccountId, "Brightwave");

        Assert.Equal("unavailable", outcome.Sections.Single(s => s.Kind == "activities").Status);
        Assert.Equal("ok", outcome.Sections.Single(s => s.Kind == "opportunities").Status);
    }

    private sealed class SlowExecutor : IQueryExecutor
    {
        private readonly IQueryExecutor _inner;
        private readonly string _slowSet;
        public SlowExecutor(IQueryExecutor inner, string slowSet) { _inner = inner; _slowSet = slowSet; }
        /// <summary>
        /// Test fake executor used by the cancellation test.
        /// </summary>
        public async Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            if (entitySetName == _slowSet) { await Task.Delay(Timeout.Infinite, ct); }
            return await _inner.ExecuteAsync(entitySetName, fetchXml, caller, ct);
        }
    }

    /// <summary>
    /// Verifies that the user's own cancel stops everything instead of becoming an unavailable section.
    /// </summary>
    [Fact]
    public async Task TheUsersOwnCancel_StopsEverything_InsteadOfBecomingAnUnavailableSection()
    {
        var executor = new FakeExecutor { Throw = new OperationCanceledException() };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Builder(executor, null, Everything()).BuildAsync(Alice, "account", AccountId, "x", cts.Token));
    }

    /// <summary>
    /// Verifies that every query runs as the caller and filters by the record.
    /// </summary>
    [Fact]
    public async Task EveryQuery_RunsAsTheCaller_AndFiltersByTheRecord()
    {
        var executor = new FakeExecutor();

        await BuildAsync(executor, Everything());

        Assert.All(executor.Calls, c => Assert.Equal(Alice, c.Caller));
        var customerQueries = executor.Calls.Where(c => c.Set is "opportunities" or "incidents" or "contracts" or "salesorders").ToList();
        Assert.All(customerQueries, c => Assert.Contains(AccountId.ToString(), c.Doc.ToString()));
    }

    /// <summary>
    /// Verifies that last contact uses a top-one query rather than an aggregate and shows none when no completed activity exists.
    /// </summary>
    [Fact]
    public async Task LastContact_UsesATopOneQuery_NotAnAggregate_AndShowsNoneWhenThereIsNoCompletedActivity()
    {
        var executor = new FakeExecutor
        {
            Override = (set, doc) => set == "activitypointers" && doc.Descendants("condition").Any(c => (string?)c.Attribute("operator") == "not-null")
                ? new ResultTable(new[] { "actualend" }, new List<IReadOnlyList<string>>())
                : null,
        };

        var outcome = await BuildAsync(executor, Account(), Activity());

        var last = outcome.Sections.Single(s => s.Kind == "activities").Metrics.Single(m => m.Key == "last_contact");
        Assert.Equal("none", last.Value);
        var lastQuery = executor.Calls.Select(c => c.Doc).Single(d => d.Descendants("condition").Any(c => (string?)c.Attribute("operator") == "not-null"));
        Assert.Null(lastQuery.Root!.Attribute("aggregate"));
        Assert.Equal("1", (string?)lastQuery.Root.Attribute("top"));
        Assert.DoesNotContain(lastQuery.Descendants("attribute"), a => a.Attribute("aggregate") is not null);
    }

    /// <summary>
    /// Verifies that expiring contracts use the ninety day window from the clock.
    /// </summary>
    [Fact]
    public async Task ExpiringContracts_UseTheNinetyDayWindowFromTheClock()
    {
        var executor = new FakeExecutor();

        await BuildAsync(executor, Account(), Contract());

        var window = executor.Calls.Select(c => c.Doc).Where(d => d.ToString().Contains("expireson")).ToList();
        Assert.Contains(window, d => d.ToString().Contains("2026-10-22") && d.ToString().Contains("2027-01-20"));
    }

    /// <summary>
    /// Verifies that the basic section shows name less metrics from the default view without IDs and duplicates.
    /// </summary>
    [Fact]
    public async Task TheBasicSection_ShowsNameLessMetrics_FromTheDefaultView_WithoutIdsAndDuplicates()
    {
        var outcome = await BuildAsync(new FakeExecutor(), Account());

        var basic = outcome.Sections.Single(s => s.Kind == "basic");
        Assert.Equal(new[] { "ownerid", "createdon", "address1_city", "telephone1" }, basic.Metrics.Select(m => m.Key));
        Assert.Contains(basic.Metrics, m => m.Label == "City");
    }

    /// <summary>
    /// Verifies that a closed status field removes the section that needs it instead of querying the closed field.
    /// </summary>
    [Fact]
    public async Task AClosedStatusField_RemovesTheSectionThatNeedsIt_InsteadOfQueryingAClosedField()
    {
        var executor = new FakeExecutor();
        var noState = new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
            new List<AttributeInfo> { A("incidentid", "Uniqueidentifier"), A("title"), A("customerid", "Lookup"), A("createdon", "DateTime") },
            new List<RelationshipInfo> { ToAccount("incident", "customerid") });

        var outcome = await BuildAsync(executor, Account(), Opportunity(), noState);

        Assert.DoesNotContain(outcome.Sections, s => s.Kind == "cases");
        Assert.DoesNotContain(executor.Calls, c => c.Set == "incidents");
        Assert.Contains(outcome.Sections, s => s.Kind == "opportunities");
    }

    /// <summary>
    /// Verifies that activities with a closed status field are left out so no closed field is filtered.
    /// </summary>
    [Fact]
    public async Task ActivitiesWithAClosedStatusField_AreLeftOut_SoNoClosedFieldIsFiltered()
    {
        var executor = new FakeExecutor();
        var noState = new EntityInfo("activitypointer", "activitypointers", "Activity", "activityid", "subject",
            new List<AttributeInfo> { A("activityid", "Uniqueidentifier"), A("subject"), A("regardingobjectid", "Lookup"), A("actualend", "DateTime"), A("createdon", "DateTime") },
            new List<RelationshipInfo> { ToAccount("activitypointer", "regardingobjectid") });

        var outcome = await BuildAsync(executor, Account(), noState);

        Assert.DoesNotContain(outcome.Sections, s => s.Kind == "activities");
        Assert.DoesNotContain(executor.Calls, c => c.Set == "activitypointers");
    }

    /// <summary>
    /// Verifies that an unknown root entity gives a single unavailable section instead of failing.
    /// </summary>
    [Fact]
    public async Task AnUnknownRootEntity_GivesASingleUnavailableSection_InsteadOfFailing()
    {
        var outcome = await Builder(new FakeExecutor(), null, Opportunity()).BuildAsync(Alice, "account", AccountId, "x");

        Assert.Equal("unavailable", Assert.Single(outcome.Sections).Status);
    }
}
