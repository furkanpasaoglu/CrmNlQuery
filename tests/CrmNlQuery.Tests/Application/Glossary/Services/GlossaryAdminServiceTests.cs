using System.Text.Json;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Glossary.Models;
using CrmNlQuery.Application.Glossary.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Glossary.Services;

/// <summary>
/// Tests the business glossary admin service: limits, validation, status, trial runs and import/export.
/// </summary>
public class GlossaryAdminServiceTests
{
    private sealed class Executor : IQueryExecutor
    {
        public string? LastFetch;
        public int Rows = 3;
        public Exception? Throw;

        /// <summary>
        /// Fake query executor that returns canned results for the test instead of calling CRM.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
        {
            LastFetch = fetchXml;
            if (Throw is not null) { throw Throw; }
            var rows = Enumerable.Range(0, Rows).Select(i => (IReadOnlyList<string>)new[] { "id" + i }).ToList();
            return Task.FromResult(new ResultTable(new[] { "accountid" }, rows));
        }
    }

    private sealed class Harness
    {
        public readonly MemoryGlossaryStore Store = new MemoryGlossaryStore();
        public readonly List<string> Audit = new List<string>();
        public readonly Executor Exec = new Executor();
        public int Max = 100;
        public List<(string Title, string Body)> Documents = new List<(string, string)>();
        public readonly GlossaryAdminService Admin;

        public Harness()
        {
            var schema = TestMetadata.Create();
            Func<string, string, string, string, CancellationToken, Task> audit = (actor, action, target, detail, ct) => { Audit.Add(actor + "|" + action + "|" + target + "|" + detail); return Task.CompletedTask; };
            var service = new GlossaryService(Store, schema, () => true, audit);
            Admin = new GlossaryAdminService(service, Store, schema, Exec, () => Max, audit, ct => Task.FromResult<IReadOnlyList<(string Title, string Body)>>(Documents));
        }
    }

    private static GlossaryEntry Active(string term) => new GlossaryEntry
    {
        Kind = GlossaryKind.Filter, Entity = "account", Terms = new[] { new GlossaryTerm(term) },
        Conditions = new[] { new GlossaryCondition("statecode", "eq", new[] { "0" }) },
    };

    /// <summary>
    /// Verifies that save adds an entry refuses duplicates and invalid ones and enforces the limit.
    /// </summary>
    [Fact]
    public async Task Save_AddsAnEntry_RefusesDuplicatesAndInvalidOnes_AndEnforcesTheLimit()
    {
        var h = new Harness();
        h.Max = 2;

        Assert.True((await h.Admin.SaveAsync("boss", Active("aktif firma"), null)).Ok);
        var duplicate = await h.Admin.SaveAsync("boss", Active("Aktif Firma"), null);
        var invalid = await h.Admin.SaveAsync("boss", Active("xyz") with { Entity = "secret" }, null);
        Assert.True((await h.Admin.SaveAsync("boss", Active("canlı firma"), null)).Ok);
        var tooMany = await h.Admin.SaveAsync("boss", Active("üçüncü"), null);

        Assert.Equal("duplicate", duplicate.Reason);
        Assert.Equal("entity_unavailable", invalid.Reason);
        Assert.Equal("limit", tooMany.Reason);
        Assert.Equal(2, (await h.Admin.ListAsync()).Count);
    }

    /// <summary>
    /// Verifies that save with an ID updates that entry and an unknown ID is not found.
    /// </summary>
    [Fact]
    public async Task Save_WithAnId_UpdatesThatEntry_AndAnUnknownIdIsNotFound()
    {
        var h = new Harness();
        var id = (await h.Admin.SaveAsync("boss", Active("aktif firma"), null)).EntryId!;

        var changed = await h.Admin.SaveAsync("boss2", Active("aktif firma") with { Description = "yeni" }, id);
        var missing = await h.Admin.SaveAsync("boss", Active("baska"), "no-such-id");

        Assert.True(changed.Ok);
        Assert.Equal("yeni", (await h.Store.GetAsync(id))!.Description);
        Assert.Equal("not_found", missing.Reason);
    }

    /// <summary>
    /// Verifies that the list shows status and counters and enabling and disabling is audited.
    /// </summary>
    [Fact]
    public async Task TheList_ShowsStatusAndCounters_AndEnablingAndDisablingIsAudited()
    {
        var h = new Harness();
        var id = (await h.Admin.SaveAsync("boss", Active("aktif firma"), null)).EntryId!;
        await h.Store.RecordAsync(id, false, DateTimeOffset.UtcNow);
        await h.Store.RecordAsync(id, true, DateTimeOffset.UtcNow);

        Assert.True((await h.Admin.SetEnabledAsync("boss", id, false)).Ok);
        var row = (await h.Admin.ListAsync()).Single();
        Assert.Equal("disabled", row.Status);
        Assert.Equal(1, row.Applied);
        Assert.Equal(1, row.NotApplied);

        await h.Admin.SetEnabledAsync("boss", id, true);
        Assert.Equal("ok", (await h.Admin.ListAsync()).Single().Status);
        Assert.Contains(h.Audit, a => a.Contains("glossary.disable")) ;
        Assert.Contains(h.Audit, a => a.Contains("glossary.enable"));
    }

    /// <summary>
    /// Verifies that an entry whose field was closed later shows as broken with a reason.
    /// </summary>
    [Fact]
    public async Task AnEntryWhoseFieldWasClosedLater_ShowsAsBroken_WithAReason()
    {
        var h = new Harness();
        await h.Store.AddAsync(Active("eski") with { Conditions = new[] { new GlossaryCondition("closedfield", "eq", new[] { "1" }) }, CreatedAt = DateTimeOffset.UtcNow, ModifiedAt = DateTimeOffset.UtcNow });

        var row = (await h.Admin.ListAsync()).Single();

        Assert.Equal("broken", row.Status);
        Assert.Equal("attribute_unavailable", row.Reason);
    }

    /// <summary>
    /// Verifies that delete removes the entry and an unknown ID is not found.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheEntry_AndAnUnknownIdIsNotFound()
    {
        var h = new Harness();
        var id = (await h.Admin.SaveAsync("boss", Active("aktif firma"), null)).EntryId!;

        Assert.True((await h.Admin.DeleteAsync("boss", id)).Ok);
        Assert.Equal("not_found", (await h.Admin.DeleteAsync("boss", id)).Reason);
        Assert.Contains(h.Audit, a => a.Contains("glossary.delete"));
    }

    /// <summary>
    /// Verifies that save names the documents that also mention the term as a warning only.
    /// </summary>
    [Fact]
    public async Task Save_NamesTheDocumentsThatAlsoMentionTheTerm_AsAWarningOnly()
    {
        var h = new Harness();
        h.Documents.Add(("Company terms", "Bir aktif firma, hesabı etkin olan firmadır."));
        h.Documents.Add(("Other", "Hiçbir ilgisi yok."));

        var result = await h.Admin.SaveAsync("boss", Active("aktif firma"), null, default, includeDocuments: true);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "Company terms" }, result.Documents);
        Assert.Single(await h.Store.ListAsync());
    }

    /// <summary>
    /// Verifies that save never names documents to a caller without the documents permission.
    /// </summary>
    [Fact]
    public async Task Save_NeverNamesDocuments_ToACallerWithoutTheDocumentsPermission()
    {
        var h = new Harness();
        h.Documents.Add(("Company terms", "Bir aktif firma, hesabı etkin olan firmadır."));

        var result = await h.Admin.SaveAsync("boss", Active("aktif firma"), null);

        Assert.True(result.Ok);
        Assert.Null(result.Documents);
    }

    /// <summary>
    /// Verifies that the summary counts entries broken ones and failed applications without any term.
    /// </summary>
    [Fact]
    public async Task TheSummary_CountsEntriesBrokenOnesAndFailedApplications_WithoutAnyTerm()
    {
        var h = new Harness();
        var id = (await h.Admin.SaveAsync("boss", Active("aktif firma"), null)).EntryId!;
        await h.Store.AddAsync(Active("eski") with { Conditions = new[] { new GlossaryCondition("closedfield", "eq", new[] { "1" }) }, CreatedAt = DateTimeOffset.UtcNow, ModifiedAt = DateTimeOffset.UtcNow });
        await h.Store.RecordAsync(id, false, DateTimeOffset.UtcNow);
        await h.Store.RecordAsync(id, false, DateTimeOffset.UtcNow);

        var summary = await h.Admin.SummaryAsync();

        Assert.Equal(new GlossarySummary(true, 2, 1, 2), summary);
    }

    /// <summary>
    /// Verifies that try counts rows with the callers rights and never returns record content.
    /// </summary>
    [Fact]
    public async Task Try_CountsRows_WithTheCallersRights_AndNeverReturnsRecordContent()
    {
        var h = new Harness();
        h.Exec.Rows = 7;

        var result = await h.Admin.TryAsync(Active("aktif firma"), Caller.ForEvaluation());

        Assert.Equal("rows", result.Outcome);
        Assert.Equal(7, result.Rows);
        Assert.False(result.Capped);
        Assert.Contains("statecode", h.Exec.LastFetch);
        Assert.Contains("top=\"500\"", h.Exec.LastFetch);
        Assert.Empty(await h.Store.ListAsync());
    }

    /// <summary>
    /// Verifies that try reports capped invalid not readable and error outcomes.
    /// </summary>
    [Fact]
    public async Task Try_ReportsCapped_Invalid_NotReadable_AndErrorOutcomes()
    {
        var h = new Harness();
        h.Exec.Rows = 500;
        Assert.True((await h.Admin.TryAsync(Active("a1"), Caller.ForEvaluation())).Capped);

        Assert.Equal("invalid", (await h.Admin.TryAsync(Active("a2") with { Entity = "secret" }, Caller.ForEvaluation())).Outcome);

        h.Exec.Throw = new CrmException("denied", 403, isPermissionDenied: true);
        Assert.Equal("not_readable", (await h.Admin.TryAsync(Active("a3"), Caller.ForEvaluation())).Outcome);

        h.Exec.Throw = new InvalidOperationException("boom");
        Assert.Equal("error", (await h.Admin.TryAsync(Active("a4"), Caller.ForEvaluation())).Outcome);
    }

    /// <summary>
    /// Verifies that try on an alias runs nothing.
    /// </summary>
    [Fact]
    public async Task Try_OnAnAlias_RunsNothing()
    {
        var h = new Harness();

        var result = await h.Admin.TryAsync(new GlossaryEntry { Kind = GlossaryKind.Alias, Entity = "account", Terms = new[] { new GlossaryTerm("müşteri") } }, Caller.ForEvaluation());

        Assert.Equal("alias", result.Outcome);
        Assert.Null(h.Exec.LastFetch);
    }

    /// <summary>
    /// Verifies that try escapes values in the generated query.
    /// </summary>
    [Fact]
    public async Task Try_EscapesValuesInTheGeneratedQuery()
    {
        var h = new Harness();
        var entry = Active("sehir") with { Conditions = new[] { new GlossaryCondition("address1_city", "like", new[] { "A\"><x/>&" }) } };

        var result = await h.Admin.TryAsync(entry, Caller.ForEvaluation());

        Assert.Equal("rows", result.Outcome);
        Assert.DoesNotContain("<x/>", h.Exec.LastFetch);
        System.Xml.Linq.XDocument.Parse(h.Exec.LastFetch!);
    }

    /// <summary>
    /// Verifies that export then import round trips into an empty glossary and the export is audited.
    /// </summary>
    [Fact]
    public async Task ExportThenImport_RoundTripsIntoAnEmptyGlossary_AndTheExportIsAudited()
    {
        var source = new Harness();
        await source.Admin.SaveAsync("boss", Active("aktif firma") with { Description = "ğüş" }, null);
        var json = await source.Admin.ExportAsync("boss");

        var target = new Harness();
        var report = await target.Admin.ImportAsync("boss", json, true);

        Assert.NotNull(report);
        Assert.Equal(1, report!.Accepted);
        Assert.Empty(report.Rejected);
        var entry = (await target.Store.ListAsync()).Single();
        Assert.Equal("aktif firma", entry.Terms[0].Text);
        Assert.Equal("ğüş", entry.Description);
        Assert.Contains(source.Audit, a => a.Contains("glossary.export"));
        Assert.Contains(target.Audit, a => a.Contains("glossary.import"));
    }

    /// <summary>
    /// Verifies that import preview reports rejections without writing anything.
    /// </summary>
    [Fact]
    public async Task ImportPreview_ReportsRejections_WithoutWritingAnything()
    {
        var h = new Harness();
        await h.Admin.SaveAsync("boss", Active("aktif firma"), null);
        var file = JsonSerializer.Serialize(new
        {
            version = 1,
            entries = new object[]
            {
                new { kind = "filter", entity = "account", terms = new[] { new { text = "yeni terim", language = "tr" } }, conditions = new[] { new { attribute = "statecode", @operator = "eq", values = new[] { "0" } } }, enabled = true },
                new { kind = "filter", entity = "account", terms = new[] { new { text = "Aktif Firma", language = "tr" } }, conditions = new[] { new { attribute = "statecode", @operator = "eq", values = new[] { "0" } } }, enabled = true },
                new { kind = "filter", entity = "account", terms = new[] { new { text = "bozuk", language = "tr" } }, conditions = new[] { new { attribute = "nope", @operator = "eq", values = new[] { "0" } } }, enabled = true },
            },
        });

        var report = await h.Admin.ImportAsync("boss", file, false);

        Assert.Equal(3, report!.Total);
        Assert.Equal(1, report.Accepted);
        Assert.False(report.Applied);
        Assert.Contains(report.Rejected, r => r.Reason == "duplicate");
        Assert.Contains(report.Rejected, r => r.Reason == "attribute_unavailable");
        Assert.Single(await h.Store.ListAsync());
    }

    /// <summary>
    /// Verifies that import honours the limit and refuses a garbage file.
    /// </summary>
    [Fact]
    public async Task Import_HonoursTheLimit_AndRefusesAGarbageFile()
    {
        var h = new Harness();
        h.Max = 1;
        var file = JsonSerializer.Serialize(new
        {
            version = 1,
            entries = new object[]
            {
                new { kind = "filter", entity = "account", terms = new[] { new { text = "bir", language = "tr" } }, conditions = new[] { new { attribute = "statecode", @operator = "eq", values = new[] { "0" } } }, enabled = true },
                new { kind = "filter", entity = "account", terms = new[] { new { text = "iki", language = "tr" } }, conditions = new[] { new { attribute = "statecode", @operator = "eq", values = new[] { "1" } } }, enabled = true },
            },
        });

        var report = await h.Admin.ImportAsync("boss", file, true);

        Assert.Equal(1, report!.Accepted);
        Assert.Contains(report.Rejected, r => r.Reason == "limit");
        Assert.Null(await h.Admin.ImportAsync("boss", "not json", true));
        Assert.Null(await h.Admin.ImportAsync("boss", "{\"version\":2,\"entries\":[]}", true));
    }
}
