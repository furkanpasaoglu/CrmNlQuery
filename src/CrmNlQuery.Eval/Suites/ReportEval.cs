using System.Text.Json;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Application.Reports.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Reports.Entities;

namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Runs the model-free report finder evaluation: intent rule, matching and catalog against the lab report list.
/// </summary>
public static class ReportEval
{
    private sealed class Case
    {
        public string Id { get; set; } = "";
        public string Question { get; set; } = "";
        public RecordContext? Record { get; set; }
        public Expect Expect { get; set; } = new Expect();
    }

    private sealed class RecordContext
    {
        public string Entity { get; set; } = "account";
        public string Name { get; set; } = "Brightwave Appliances Ltd";
    }

    private sealed class Expect
    {
        public bool Report { get; set; } = true;
        public string? First { get; set; }
        public bool? ListedAll { get; set; }
        public string? UrlContains { get; set; }
        public string? ForRecord { get; set; }
    }

    private sealed class File
    {
        public List<CatalogEntry> Catalog { get; set; } = new List<CatalogEntry>();
        public List<Case> Cases { get; set; } = new List<Case>();
    }

    private sealed class CatalogEntry
    {
        public string Name { get; set; } = "";
        public List<string> Keywords { get; set; } = new List<string>();
        public string? CrmReportId { get; set; }
        public string? Url { get; set; }
        public string? RecordEntity { get; set; }
        public string? RecordParam { get; set; }
    }

    private sealed class RunnerExecutor : IQueryExecutor
    {
        private readonly Func<string, CancellationToken, Task<ResultTable>> _run;

        public RunnerExecutor(Func<string, CancellationToken, Task<ResultTable>> run)
        {
            _run = run;
        }

        /// <summary>
        /// Executes report list queries by delegating to a supplied runner function.
        /// </summary>
        public Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default) => _run(fetchXml, ct);
    }

    private sealed class ListStore : IReportCatalogStore
    {
        private readonly List<ReportCatalogEntry> _entries = new List<ReportCatalogEntry>();

        public bool Persistent => false;
        /// <summary>
        /// Returns the in-memory list of report catalog entries.
        /// </summary>
        public Task<IReadOnlyList<ReportCatalogEntry>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReportCatalogEntry>>(_entries.ToList());
        /// <summary>
        /// Returns one report catalog entry by id from the in-memory list.
        /// </summary>
        public Task<ReportCatalogEntry?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));
        /// <summary>
        /// Inserts or replaces a catalog entry in the in-memory list.
        /// </summary>
        public Task UpsertAsync(ReportCatalogEntry entry, CancellationToken ct = default)
        {
            _entries.RemoveAll(e => e.Id == entry.Id);
            _entries.Add(entry);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Removes a catalog entry from the in-memory list.
        /// </summary>
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_entries.RemoveAll(e => e.Id == id) > 0);
    }

    private sealed class FixedCodes : IObjectTypeCodeSource
    {
        /// <summary>
        /// Returns fixed object type codes for entities in the test.
        /// </summary>
        public Task<int?> GetAsync(string entityLogicalName, CancellationToken ct = default) => Task.FromResult<int?>(entityLogicalName == "account" ? 1 : null);
    }

    /// <summary>
    /// Runs the report finder cases against a metadata source and report query runner and returns the outcomes.
    /// </summary>
    public static async Task RunAsync(IMetadataSource metadata, Func<string, CancellationToken, Task<ResultTable>> run, string crmBaseUrl, string path, string? onlyCase)
    {
        var file = JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var store = new ListStore();
        var catalog = new ReportCatalogService(store, () => 200, (a, b, c, d, ct) => Task.CompletedTask);
        foreach (var entry in file.Catalog)
        {
            var saved = await catalog.SaveAsync("eval", new ReportCatalogInput(null, entry.Name, null, entry.Keywords, entry.CrmReportId, entry.Url, entry.RecordEntity, entry.RecordParam, true));
            if (!saved.Ok) { throw new InvalidOperationException($"Catalog entry '{entry.Name}' is invalid: {saved.Error}"); }
        }
        var service = new ReportSearchService(new RunnerExecutor(run), metadata, () => true, crmBaseUrl, store, new FixedCodes());

        var passed = 0;
        var total = 0;
        foreach (var c in file.Cases.Where(c => onlyCase is null || c.Id == onlyCase))
        {
            total++;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string verdict;
            try
            {
                var record = c.Record is null ? null : new ResolvedRecord("this", c.Record.Entity, Guid.Parse("465bf4cc-67d0-f111-b4eb-00155d01f303"), c.Record.Name);
                var outcome = await service.TryFindAsync(c.Question, Caller.ForEvaluation(), record);
                verdict = Check(c.Expect, outcome) ?? "ok";
            }
            catch (Exception ex)
            {
                verdict = "EXCEPTION " + ex.GetType().Name + ": " + ex.Message;
            }
            var ok = verdict == "ok";
            if (ok) { passed++; }
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {c.Id,-34} {verdict}  ({watch.Elapsed.TotalSeconds:0.0}s)");
        }
        Console.WriteLine($"\nSummary: {passed}/{total}");
    }

    private static string? Check(Expect e, CrmNlQuery.Application.Ask.Models.ReportOutcome? outcome)
    {
        if (!e.Report)
        {
            return outcome is null ? null : $"expected a normal question but reports were offered: {string.Join(", ", outcome.Reports.Select(r => r.Name))}";
        }
        if (outcome is null || outcome.Reports.Count == 0) { return "expected reports but none were offered"; }
        if (e.First is not null && !string.Equals(outcome.Reports[0].Name, e.First, StringComparison.CurrentCultureIgnoreCase))
        {
            return $"first report was '{outcome.Reports[0].Name}', expected '{e.First}'";
        }
        if (e.ListedAll is not null && outcome.ListedAll != e.ListedAll) { return $"listedAll was {outcome.ListedAll}"; }
        if (e.UrlContains is not null && !outcome.Reports[0].Url.Contains(e.UrlContains, StringComparison.OrdinalIgnoreCase)) { return $"url '{outcome.Reports[0].Url}' lacks '{e.UrlContains}'"; }
        if (e.ForRecord is not null && outcome.Reports[0].ForRecord != e.ForRecord) { return $"forRecord was '{outcome.Reports[0].ForRecord}'"; }
        return null;
    }
}
