using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Reports.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Reports.Services;

/// <summary>
/// Tests report finding with the caller's permissions: listing, matching, addresses, caching and the closed-table cases.
/// </summary>
public class ReportSearchServiceTests
{
    private static readonly Guid Neglected = Guid.Parse("7dce8a08-7d04-e711-80e6-00155db8652a");
    private static readonly Guid Summary = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class Metadata : IMetadataSource
    {
        public bool ReportTableOpen = true;
        private readonly EntityInfo _report = new EntityInfo("report", "reports", "Report", "reportid", "name",
            new List<AttributeInfo> { new AttributeInfo("name", "Name", "String", new List<OptionInfo>()) }, new List<RelationshipInfo>());

        /// <summary>
        /// Test metadata source that lists no entities, as the report search tests only need the single report entity lookup.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntitySummary>>(new List<EntitySummary>());

        /// <summary>
        /// Returns the test report entity only when the logical name is report and the simulated report table is open, otherwise null.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default)
            => Task.FromResult<EntityInfo?>(logicalName == "report" && ReportTableOpen ? _report : null);

        /// <summary>
        /// Does nothing in this test metadata source, since schema refresh is irrelevant to report search tests.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Executor : IQueryExecutor
    {
        public int Calls;
        public string? LastFetch;
        public string? LastEntitySet;
        public bool Denied;
        public bool Broken;

        /// <summary>
        /// Returns a canned report list for the report finder tests.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            Calls++;
            LastFetch = fetchXml;
            LastEntitySet = entitySetName;
            if (Denied) { throw new CrmException("no read on report", isPermissionDenied: true); }
            if (Broken) { throw new HttpRequestException("down"); }
            var rows = new List<IReadOnlyList<string>>
            {
                new[] { "Neglected Cases", "Shows cases that nobody worked on\r\nfor a number of days" },
                new[] { "Account Summary", "" },
                new[] { "No Id Report", "x" },
            };
            return Task.FromResult(new ResultTable(new[] { "Name", "Description" }, rows, new[] { Neglected.ToString("D"), Summary.ToString("D"), "not-a-guid" }));
        }
    }

    private sealed class Harness
    {
        public readonly Metadata Metadata = new Metadata();
        public readonly Executor Executor = new Executor();
        public bool Enabled = true;
        public DateTimeOffset Now = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
        public ReportSearchService Service;

        public Harness()
        {
            Service = new ReportSearchService(Executor, Metadata, () => Enabled, "http://crm.example.com/contoso/", clock: () => Now);
        }

        /// <summary>
        /// Asks the report finder a question as a given user and returns its outcome.
        /// </summary>
        public Task<CrmNlQuery.Application.Ask.Models.ReportOutcome?> Ask(string question, Guid? user = null) => Service.TryFindAsync(question, Caller.User(user ?? Alice), null, CancellationToken.None);
    }

    /// <summary>
    /// Verifies that a matching report is returned with the viewer address.
    /// </summary>
    [Fact]
    public async Task AMatchingReportIsReturnedWithTheViewerAddress()
    {
        var h = new Harness();

        var outcome = await h.Ask("Neglected Cases raporunu aç");

        var item = Assert.Single(outcome!.Reports);
        Assert.Equal("Neglected Cases", item.Name);
        Assert.Equal("Shows cases that nobody worked on for a number of days", item.Description);
        Assert.Equal("http://crm.example.com/contoso/crmreports/viewer/viewer.aspx?action=run&id=%7b7dce8a08-7d04-e711-80e6-00155db8652a%7d", item.Url);
        Assert.False(outcome.ListedAll);
    }

    /// <summary>
    /// Verifies that only top level rdl and link reports are requested through the reports entity set.
    /// </summary>
    [Fact]
    public async Task OnlyTopLevelRdlAndLinkReportsAreRequested_ThroughTheReportsEntitySet()
    {
        var h = new Harness();

        await h.Ask("Account Summary raporunu aç");

        Assert.Equal("reports", h.Executor.LastEntitySet);
        Assert.Contains("attribute='parentreportid' operator='null'", h.Executor.LastFetch);
        Assert.Contains("<value>1</value><value>3</value>", h.Executor.LastFetch);
    }

    /// <summary>
    /// Verifies that rows without a valid ID are skipped.
    /// </summary>
    [Fact]
    public async Task RowsWithoutAValidIdAreSkipped()
    {
        var h = new Harness();

        var outcome = await h.Ask("raporları göster");

        Assert.True(outcome!.ListedAll);
        Assert.Equal(new[] { "Account Summary", "Neglected Cases" }, outcome.Reports.Select(r => r.Name).ToArray());
    }

    /// <summary>
    /// Verifies that non report questions never touch the CRM.
    /// </summary>
    [Theory]
    [InlineData("Show accounts in London")]
    [InlineData("How many reports are there")]
    public async Task NonReportQuestionsNeverTouchTheCrm(string question)
    {
        var h = new Harness();

        Assert.Null(await h.Ask(question));
        Assert.Equal(0, h.Executor.Calls);
    }

    /// <summary>
    /// Verifies that a name that matches nothing returns null so the question continues normally.
    /// </summary>
    [Fact]
    public async Task ANameThatMatchesNothingReturnsNull_SoTheQuestionContinuesNormally()
    {
        var h = new Harness();

        Assert.Null(await h.Ask("Zzxyqq raporunu aç"));
    }

    /// <summary>
    /// Verifies that a closed report table disables the feature quietly.
    /// </summary>
    [Fact]
    public async Task AClosedReportTableDisablesTheFeatureQuietly()
    {
        var h = new Harness();
        h.Metadata.ReportTableOpen = false;

        Assert.Null(await h.Ask("Neglected Cases raporunu aç"));
        Assert.Equal(0, h.Executor.Calls);
    }

    /// <summary>
    /// Verifies that the setting turns the feature off.
    /// </summary>
    [Fact]
    public async Task TheSettingTurnsTheFeatureOff()
    {
        var h = new Harness { Enabled = false };

        Assert.Null(await h.Ask("Neglected Cases raporunu aç"));
        Assert.Equal(0, h.Executor.Calls);
    }

    /// <summary>
    /// Verifies that a failing or denied read falls through instead of breaking the question.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AFailingOrDeniedReadFallsThroughInsteadOfBreakingTheQuestion(bool denied, bool broken)
    {
        var h = new Harness();
        h.Executor.Denied = denied;
        h.Executor.Broken = broken;

        Assert.Null(await h.Ask("Neglected Cases raporunu aç"));
    }

    /// <summary>
    /// Verifies that the list is cached per user for five minutes.
    /// </summary>
    [Fact]
    public async Task TheListIsCachedPerUserForFiveMinutes()
    {
        var h = new Harness();

        await h.Ask("Neglected Cases raporunu aç");
        await h.Ask("Account Summary raporunu aç");
        Assert.Equal(1, h.Executor.Calls);

        await h.Ask("Neglected Cases raporunu aç", Bob);
        Assert.Equal(2, h.Executor.Calls);

        h.Now = h.Now.AddMinutes(6);
        await h.Ask("Neglected Cases raporunu aç");
        Assert.Equal(3, h.Executor.Calls);
    }
}
