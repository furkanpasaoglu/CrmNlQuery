using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Application.Reports.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Application.Reports;

/// <summary>
/// Unit tests for the report catalog: validation, alias and keyword matching, external addresses, record-context links and per-user report visibility.
/// </summary>
public class ReportCatalogTests
{
    private static readonly Guid AccountOverview = Guid.Parse("2b2a692e-7d04-e711-80e6-00155db8652a");
    private static readonly Guid Neglected = Guid.Parse("7dce8a08-7d04-e711-80e6-00155db8652a");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BrightwaveId = Guid.Parse("465bf4cc-67d0-f111-b4eb-00155d01f303");

    private sealed class OpenMetadata : IMetadataSource
    {
        public bool ReportTableOpen = true;
        private readonly EntityInfo _report = new EntityInfo("report", "reports", "Report", "reportid", "name",
            new List<AttributeInfo> { new AttributeInfo("name", "Name", "String", new List<OptionInfo>()) }, new List<RelationshipInfo>());

        /// <summary>
        /// Test double that list entities with canned data instead of calling a real dependency.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntitySummary>>(new List<EntitySummary>());

        /// <summary>
        /// Test double metadata source that returns a fixed entity definition so catalog tests run without a real CRM.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default)
            => Task.FromResult<EntityInfo?>(logicalName == "report" && ReportTableOpen ? _report : null);

        /// <summary>
        /// Test double that refresh with canned data instead of calling a real dependency.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class PerUserExecutor : IQueryExecutor
    {
        public readonly Dictionary<Guid, List<(string Name, Guid Id)>> Visible = new Dictionary<Guid, List<(string Name, Guid Id)>>();

        /// <summary>
        /// Test double that execute with canned data instead of calling a real dependency.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            var list = Visible.TryGetValue(caller.UserId, out var l) ? l : new List<(string Name, Guid Id)>();
            var rows = list.Select(r => (IReadOnlyList<string>)new[] { r.Name, "" }).ToList();
            return Task.FromResult(new ResultTable(new[] { "Name", "Description" }, rows, list.Select(r => r.Id.ToString("D")).ToList()));
        }
    }

    private sealed class Codes : IObjectTypeCodeSource
    {
        /// <summary>
        /// Test double object-type-code lookup that returns canned codes for entity names instead of querying the CRM.
        /// </summary>
        public Task<int?> GetAsync(string entityLogicalName, CancellationToken ct = default) => Task.FromResult<int?>(entityLogicalName == "account" ? 1 : null);
    }

    private sealed class Rig
    {
        public readonly MemoryReportCatalogStore Store = new MemoryReportCatalogStore();
        public readonly PerUserExecutor Executor = new PerUserExecutor();
        public readonly OpenMetadata Metadata = new OpenMetadata();
        public readonly List<string> Audit = new List<string>();
        public readonly ReportSearchService Search;
        public readonly ReportCatalogService Catalog;

        public Rig(int max = 200)
        {
            Executor.Visible[Alice] = new List<(string, Guid)> { ("Account Overview", AccountOverview), ("Neglected Cases", Neglected) };
            Search = new ReportSearchService(Executor, Metadata, () => true, "http://crm/org", Store, new Codes());
            Catalog = new ReportCatalogService(Store, () => max, (actor, action, target, detail, ct) => { Audit.Add(action + ":" + actor); return Task.CompletedTask; });
        }

        /// <summary>
        /// Test double that ask with canned data instead of calling a real dependency.
        /// </summary>
        public Task<ReportOutcome?> Ask(string q, ResolvedRecord? record = null) => Search.TryFindAsync(q, Caller.User(Alice), record);
    }

    private static ReportCatalogInput Crm(string name, params string[] keywords)
        => new ReportCatalogInput(null, name, null, keywords, AccountOverview.ToString("D"), null, null, null, true);

    /// <summary>
    /// Verifies that a catalog alias with keywords finds the CRM report by the company name.
    /// </summary>
    [Fact]
    public async Task ACatalogAliasWithKeywordsFindsTheCrmReportByTheCompanyName()
    {
        var rig = new Rig();
        await rig.Catalog.SaveAsync("admin", Crm("Hesap özeti", "müşteri raporu"));

        var outcome = await rig.Ask("hesap özeti raporunu aç");

        var item = Assert.Single(outcome!.Reports);
        Assert.Equal("Hesap özeti", item.Name);
        Assert.Contains(AccountOverview.ToString("D"), item.Url);
    }

    /// <summary>
    /// Verifies that a keyword counts like the name.
    /// </summary>
    [Fact]
    public async Task AKeywordCountsLikeTheName()
    {
        var rig = new Rig();
        await rig.Catalog.SaveAsync("admin", Crm("Hesap özeti", "yönetici panosu"));

        var outcome = await rig.Ask("yönetici panosu raporunu göster");

        Assert.Equal("Hesap özeti", Assert.Single(outcome!.Reports).Name);
    }

    /// <summary>
    /// Verifies that an alias for a report the user cannot see is not offered.
    /// </summary>
    [Fact]
    public async Task AnAliasForAReportTheUserCannotSeeIsNotOffered()
    {
        var rig = new Rig();
        rig.Executor.Visible[Alice] = new List<(string, Guid)> { ("Neglected Cases", Neglected) };
        await rig.Catalog.SaveAsync("admin", Crm("Hesap özeti"));

        var outcome = await rig.Ask("hesap özeti raporunu aç");

        Assert.Null(outcome);
    }

    /// <summary>
    /// Verifies that an alias replaces the original entry instead of listing both.
    /// </summary>
    [Fact]
    public async Task AnAliasReplacesTheOriginalEntryInsteadOfListingBoth()
    {
        var rig = new Rig();
        await rig.Catalog.SaveAsync("admin", Crm("Hesap özeti"));

        var outcome = await rig.Ask("raporları göster");

        Assert.Equal(1, outcome!.Reports.Count(r => r.Url.Contains(AccountOverview.ToString("D"))));
        Assert.DoesNotContain(outcome.Reports, r => r.Name == "Account Overview");
    }

    /// <summary>
    /// Verifies that an external report works even when the CRM report table is closed.
    /// </summary>
    [Fact]
    public async Task AnExternalReportWorksEvenWhenTheCrmReportTableIsClosed()
    {
        var rig = new Rig();
        rig.Metadata.ReportTableOpen = false;
        await rig.Catalog.SaveAsync("admin", new ReportCatalogInput(null, "Aylık satış", null, new[] { "satış" }, null, "https://reports.example/sales", null, null, true));

        var outcome = await rig.Ask("aylık satış raporunu aç");

        var item = Assert.Single(outcome!.Reports);
        Assert.Equal("https://reports.example/sales", item.Url);
    }

    /// <summary>
    /// Verifies that a disabled entry is ignored.
    /// </summary>
    [Fact]
    public async Task ADisabledEntryIsIgnored()
    {
        var rig = new Rig();
        await rig.Catalog.SaveAsync("admin", new ReportCatalogInput(null, "Aylık satış", null, null, null, "https://reports.example/sales", null, null, false));

        Assert.Null(await rig.Ask("aylık satış raporunu aç"));
    }

    /// <summary>
    /// Verifies that on a record form a CRM report opens for that record.
    /// </summary>
    [Fact]
    public async Task OnARecordFormACrmReportOpensForThatRecord()
    {
        var rig = new Rig();
        await rig.Catalog.SaveAsync("admin", new ReportCatalogInput(null, "Hesap özeti", null, null, AccountOverview.ToString("D"), null, "account", null, true));

        var outcome = await rig.Ask("bu hesap özeti raporunu aç", new ResolvedRecord("this", "account", BrightwaveId, "Brightwave"));

        var item = Assert.Single(outcome!.Reports);
        Assert.Contains("&context=records&recordstype=1&records=%7b" + BrightwaveId.ToString("D") + "%7d", item.Url);
        Assert.Equal("Brightwave", item.ForRecord);
    }

    /// <summary>
    /// Verifies that without a record or for another table the report opens plain.
    /// </summary>
    [Fact]
    public async Task WithoutARecordOrForAnotherTableTheReportOpensPlain()
    {
        var rig = new Rig();
        await rig.Catalog.SaveAsync("admin", new ReportCatalogInput(null, "Hesap özeti", null, null, AccountOverview.ToString("D"), null, "account", null, true));

        var plain = await rig.Ask("hesap özeti raporunu aç");
        var other = await rig.Ask("hesap özeti raporunu aç", new ResolvedRecord("this", "contact", Guid.NewGuid(), "Kişi"));

        Assert.DoesNotContain("records=", Assert.Single(plain!.Reports).Url);
        Assert.DoesNotContain("records=", Assert.Single(other!.Reports).Url);
        Assert.Null(Assert.Single(plain.Reports).ForRecord);
    }

    /// <summary>
    /// Verifies that an external report gets the record ID as a parameter.
    /// </summary>
    [Fact]
    public async Task AnExternalReportGetsTheRecordIdAsAParameter()
    {
        var rig = new Rig();
        await rig.Catalog.SaveAsync("admin", new ReportCatalogInput(null, "Dış hesap raporu", null, null, null, "https://reports.example/acc?lang=tr", "account", "accountId", true));

        var outcome = await rig.Ask("dış hesap raporunu aç", new ResolvedRecord("this", "account", BrightwaveId, "Brightwave"));

        Assert.Equal("https://reports.example/acc?lang=tr&accountId=" + BrightwaveId.ToString("D"), Assert.Single(outcome!.Reports).Url);
    }

    /// <summary>
    /// Verifies that a name is required.
    /// </summary>
    [Theory]
    [InlineData(null, "name")]
    [InlineData("", "name")]
    public void ANameIsRequired(string? name, string error)
    {
        Assert.Equal(error, ReportCatalogService.Validate(new ReportCatalogInput(null, name, null, null, null, "https://a.example/x", null, null, true)).Error);
    }

    /// <summary>
    /// Verifies that exactly one target is required.
    /// </summary>
    [Fact]
    public void ExactlyOneTargetIsRequired()
    {
        var both = new ReportCatalogInput(null, "A", null, null, AccountOverview.ToString("D"), "https://a.example/x", null, null, true);
        var none = new ReportCatalogInput(null, "A", null, null, null, null, null, null, true);

        Assert.Equal("target", ReportCatalogService.Validate(both).Error);
        Assert.Equal("target", ReportCatalogService.Validate(none).Error);
    }

    /// <summary>
    /// Verifies that only plain HTTP addresses are accepted.
    /// </summary>
    [Theory]
    [InlineData("ftp://a.example/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@a.example/x")]
    [InlineData("/relative/path")]
    public void OnlyPlainHttpAddressesAreAccepted(string url)
    {
        Assert.Equal("url", ReportCatalogService.Validate(new ReportCatalogInput(null, "A", null, null, null, url, null, null, true)).Error);
    }

    /// <summary>
    /// Verifies that a CRM report ID must be a GUID.
    /// </summary>
    [Fact]
    public void ACrmReportIdMustBeAGuid()
    {
        Assert.Equal("crm_id", ReportCatalogService.Validate(new ReportCatalogInput(null, "A", null, null, "not-a-guid", null, null, null, true)).Error);
    }

    /// <summary>
    /// Verifies that an external report for a record needs the parameter name.
    /// </summary>
    [Fact]
    public void AnExternalReportForARecordNeedsTheParameterName()
    {
        var input = new ReportCatalogInput(null, "A", null, null, null, "https://a.example/x", "account", null, true);

        Assert.Equal("record_param", ReportCatalogService.Validate(input).Error);
    }

    /// <summary>
    /// Verifies that keywords are trimmed and deduplicated.
    /// </summary>
    [Fact]
    public void KeywordsAreTrimmedAndDeduplicated()
    {
        var (error, entry) = ReportCatalogService.Validate(new ReportCatalogInput(null, "A", null, new[] { " satış ", "Satış", "", "gelir" }, null, "https://a.example/x", null, null, true));

        Assert.Null(error);
        Assert.Equal(new[] { "satış", "gelir" }, entry!.Keywords);
    }

    /// <summary>
    /// Verifies that the catalog has a size limit and changes are audited.
    /// </summary>
    [Fact]
    public async Task TheCatalogHasASizeLimitAndChangesAreAudited()
    {
        var rig = new Rig(max: 1);

        var first = await rig.Catalog.SaveAsync("admin", Crm("Bir"));
        var second = await rig.Catalog.SaveAsync("admin", Crm("Iki"));
        var deleted = await rig.Catalog.DeleteAsync("admin", first.Entry!.Id);

        Assert.True(first.Ok);
        Assert.Equal("limit", second.Error);
        Assert.True(deleted);
        Assert.Equal(new[] { "reports.create:admin", "reports.delete:admin" }, rig.Audit);
    }

    /// <summary>
    /// Verifies that updating an unknown entry fails.
    /// </summary>
    [Fact]
    public async Task UpdatingAnUnknownEntryFails()
    {
        var rig = new Rig();

        var result = await rig.Catalog.SaveAsync("admin", Crm("A") with { Id = "yok" });

        Assert.Equal("not_found", result.Error);
    }
}
