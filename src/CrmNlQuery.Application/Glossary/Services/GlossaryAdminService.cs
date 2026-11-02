using System.Text.Json;
using System.Xml.Linq;
using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Glossary.Abstractions;
using CrmNlQuery.Application.Glossary.Models;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;

namespace CrmNlQuery.Application.Glossary.Services;

/// <summary>
/// Admin operations for the glossary: listing, saving with validation, enabling, deleting, trying, and JSON import and export.
/// </summary>
public sealed class GlossaryAdminService
{
    public const int TryLimit = 500;

    private readonly GlossaryService _glossary;
    private readonly IGlossaryStore _store;
    private readonly CrmNlQuery.Application.Ask.Abstractions.IMetadataSource _metadata;
    private readonly IQueryExecutor _executor;
    private readonly Func<int> _maxEntries;
    private readonly Func<string, string, string, string, CancellationToken, Task> _audit;
    private readonly Func<CancellationToken, Task<IReadOnlyList<(string Title, string Body)>>>? _documents;

    public GlossaryAdminService(GlossaryService glossary, IGlossaryStore store, CrmNlQuery.Application.Ask.Abstractions.IMetadataSource metadata, IQueryExecutor executor, Func<int> maxEntries,
        Func<string, string, string, string, CancellationToken, Task> audit,
        Func<CancellationToken, Task<IReadOnlyList<(string Title, string Body)>>>? documents = null)
    {
        _documents = documents;
        _glossary = glossary;
        _store = store;
        _metadata = metadata;
        _executor = executor;
        _maxEntries = maxEntries;
        _audit = audit;
    }

    public bool Enabled => _glossary.Enabled;
    public bool Persistent => _store.Persistent;
    public int MaxEntries => _maxEntries();

    /// <summary>
    /// Lists glossary entries with their status and usage counters.
    /// </summary>
    public async Task<IReadOnlyList<GlossaryRow>> ListAsync(CancellationToken ct = default)
    {
        var entries = await _store.ListAsync(ct);
        var stats = (await _store.StatsAsync(ct)).ToDictionary(s => s.EntryId);
        var rows = new List<GlossaryRow>();
        foreach (var entry in entries)
        {
            var check = await GlossaryValidator.CheckAsync(entry, _metadata, ct);
            var status = !entry.Enabled ? "disabled" : check.Ok ? "ok" : "broken";
            stats.TryGetValue(entry.Id, out var s);
            rows.Add(new GlossaryRow(entry, status, check.Ok ? null : check.Reason, s?.Applied ?? 0, s?.NotApplied ?? 0, s?.LastNotAppliedAt));
        }
        return rows;
    }

    /// <summary>
    /// Returns the tables available for the glossary form from the filtered schema, with closed tables and fields absent.
    /// </summary>
    public async Task<IReadOnlyList<CrmNlQuery.Domain.Metadata.Entities.EntitySummary>> EntitiesAsync(CancellationToken ct = default) => await _metadata.ListEntitiesAsync(ct);

    /// <summary>
    /// Returns the fields of one table for the glossary form, with type and options.
    /// </summary>
    public async Task<CrmNlQuery.Domain.Metadata.Entities.EntityInfo?> EntityAsync(string entity, CancellationToken ct = default) => await _metadata.GetEntityAsync((entity ?? "").Trim().ToLowerInvariant(), ct);

    /// <summary>
    /// Saves a new or updated glossary entry after limit, validation and uniqueness checks, optionally listing conflicting document titles.
    /// </summary>
    public async Task<GlossaryAdminResult> SaveAsync(string actor, GlossaryEntry draft, string? id, CancellationToken ct = default, bool includeDocuments = false)
    {
        GlossarySaveResult result;
        if (string.IsNullOrWhiteSpace(id))
        {
            if ((await _store.ListAsync(ct)).Count >= _maxEntries()) { return GlossaryAdminResult.Fail("limit"); }
            result = await _glossary.AddAsync(Clean(draft), actor, ct);
        }
        else
        {
            result = await _glossary.UpdateAsync(Clean(draft) with { Id = id }, actor, ct);
        }
        return result.Status switch
        {
            GlossarySaveStatus.Saved => GlossaryAdminResult.Success(result.EntryId, includeDocuments ? await MentioningDocumentsAsync(Clean(draft), ct) : null),
            GlossarySaveStatus.Duplicate => GlossaryAdminResult.Fail("duplicate", result.Reason),
            GlossarySaveStatus.NotFound => GlossaryAdminResult.Fail("not_found"),
            _ => GlossaryAdminResult.Fail(result.Reason ?? "terms_invalid"),
        };
    }

    /// <summary>
    /// Returns the glossary counts used by the dashboard box.
    /// </summary>
    public async Task<GlossarySummary> SummaryAsync(CancellationToken ct = default)
    {
        var rows = await ListAsync(ct);
        return new GlossarySummary(_glossary.Enabled, rows.Count, rows.Count(r => r.Status == "broken"), rows.Sum(r => r.NotApplied));
    }

    private async Task<IReadOnlyList<string>> MentioningDocumentsAsync(GlossaryEntry entry, CancellationToken ct)
    {
        if (_documents is null) { return Array.Empty<string>(); }
        try
        {
            var docs = await _documents(ct);
            var probe = entry.Terms.Select(t => new GlossaryEntry { Id = "probe", Kind = GlossaryKind.Alias, Entity = "x", Terms = new[] { t } }).ToList();
            return docs.Where(d => GlossaryMatcher.Match(d.Title + " " + d.Body, probe).Count > 0).Select(d => d.Title).Take(5).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Enables or disables a glossary entry and records the change.
    /// </summary>
    public async Task<GlossaryAdminResult> SetEnabledAsync(string actor, string id, bool enabled, CancellationToken ct = default)
    {
        var existing = await _store.GetAsync(id, ct);
        if (existing is null) { return GlossaryAdminResult.Fail("not_found"); }
        var result = await _glossary.UpdateAsync(existing with { Enabled = enabled }, actor, ct);
        return result.Status == GlossarySaveStatus.Saved ? GlossaryAdminResult.Success(id) : GlossaryAdminResult.Fail(result.Reason ?? "invalid");
    }

    /// <summary>
    /// Deletes a glossary entry and records the deletion.
    /// </summary>
    public async Task<GlossaryAdminResult> DeleteAsync(string actor, string id, CancellationToken ct = default)
    {
        return await _glossary.DeleteAsync(id, actor, ct) ? GlossaryAdminResult.Success(id) : GlossaryAdminResult.Fail("not_found");
    }

    private static GlossaryEntry Clean(GlossaryEntry draft) => draft with
    {
        Entity = (draft.Entity ?? "").Trim().ToLowerInvariant(),
        Attribute = string.IsNullOrWhiteSpace(draft.Attribute) ? null : draft.Attribute!.Trim().ToLowerInvariant(),
        Description = (draft.Description ?? "").Trim(),
        Terms = draft.Terms.Where(t => !string.IsNullOrWhiteSpace(t.Text)).Select(t => new GlossaryTerm(t.Text.Trim(), t.Language is "tr" or "en" ? t.Language : "any")).ToList(),
        Conditions = draft.Conditions.Select(c => new GlossaryCondition((c.Attribute ?? "").Trim().ToLowerInvariant(), (c.Operator ?? "").Trim().ToLowerInvariant(), c.Values.Select(v => (v ?? "").Trim()).Where(v => v.Length > 0).ToList())).ToList(),
    };

    /// <summary>
    /// Validates a draft entry without saving and counts the rows its filter would return with the admin's own CRM permissions.
    /// </summary>
    public async Task<GlossaryTryResult> TryAsync(GlossaryEntry draft, Caller caller, CancellationToken ct = default)
    {
        var entry = Clean(draft);
        var check = await GlossaryValidator.CheckAsync(entry, _metadata, ct);
        if (!check.Ok) { return new GlossaryTryResult("invalid", Reason: check.Reason); }
        var info = await _metadata.GetEntityAsync(entry.Entity, ct);
        if (info is null) { return new GlossaryTryResult("invalid", Reason: "entity_unavailable"); }
        var text = GlossaryExplain.Describe(entry, info);
        if (entry.Kind != GlossaryKind.Filter) { return new GlossaryTryResult("alias", Text: text); }

        using var accessScope = AccessLogContext.Begin("other");
        try
        {
            var table = await _executor.ExecuteAsync(info.EntitySetName, FetchFor(entry, info), caller, ct);
            return new GlossaryTryResult("rows", table.Rows.Count, table.Rows.Count >= TryLimit, Text: text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CrmNlQuery.Application.Common.Exceptions.CrmException ex) when (ex.IsPermissionDenied)
        {
            return new GlossaryTryResult("not_readable", Text: text);
        }
        catch (Exception)
        {
            return new GlossaryTryResult("error", Text: text);
        }
    }

    /// <summary>
    /// Builds the FetchXML for a glossary entry, setting values as XML elements so admin text is never inserted raw.
    /// </summary>
    internal static string FetchFor(GlossaryEntry entry, CrmNlQuery.Domain.Metadata.Entities.EntityInfo info)
    {
        var filter = new XElement("filter", new XAttribute("type", "and"));
        foreach (var c in entry.Conditions)
        {
            var condition = new XElement("condition", new XAttribute("attribute", c.Attribute), new XAttribute("operator", c.Operator == GlossaryOperator.Like ? "like" : c.Operator));
            if (c.Operator == GlossaryOperator.In || c.Operator == GlossaryOperator.NotIn)
            {
                foreach (var v in c.Values) { condition.Add(new XElement("value", v)); }
            }
            else if (!GlossaryOperator.NeedsNoValue(c.Operator))
            {
                var value = c.Values[0];
                condition.Add(new XAttribute("value", c.Operator == GlossaryOperator.Like ? value.TrimEnd('%') + "%" : value));
            }
            filter.Add(condition);
        }
        var entity = new XElement("entity", new XAttribute("name", entry.Entity), new XElement("attribute", new XAttribute("name", info.PrimaryIdAttribute)), filter);
        return new XElement("fetch", new XAttribute("top", TryLimit), entity).ToString(SaveOptions.DisableFormatting);
    }

    private sealed record TermJson(string Text, string Language);

    private sealed record ConditionJson(string Attribute, string Operator, List<string> Values);

    private sealed record EntryJson(string Kind, List<TermJson> Terms, string Entity, string? Attribute, List<ConditionJson> Conditions, string Description, bool Enabled);

    private sealed record FileJson(int Version, List<EntryJson> Entries);

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Exports all glossary entries as a versioned JSON document containing terms, languages, entity, attribute and conditions.
    /// </summary>
    public async Task<string> ExportAsync(string actor, CancellationToken ct = default)
    {
        var entries = await _store.ListAsync(ct);
        var file = new FileJson(1, entries.Select(e => new EntryJson(e.Kind, e.Terms.Select(t => new TermJson(t.Text, t.Language)).ToList(), e.Entity, e.Attribute,
            e.Conditions.Select(c => new ConditionJson(c.Attribute, c.Operator, c.Values.ToList())).ToList(), e.Description, e.Enabled)).ToList());
        await _audit(actor, "glossary.export", "glossary", $"{entries.Count} entries", ct);
        return JsonSerializer.Serialize(file, JsonOptions);
    }

    /// <summary>
    /// Validates a JSON import or, when applying, adds the accepted entries and skips rejected ones.
    /// </summary>
    public async Task<GlossaryImportReport?> ImportAsync(string actor, string json, bool apply, CancellationToken ct = default)
    {
        FileJson? file;
        try { file = JsonSerializer.Deserialize<FileJson>(json, JsonOptions); }
        catch (JsonException) { return null; }
        if (file is null || file.Entries is null || file.Version != 1) { return null; }

        var existing = (await _store.ListAsync(ct)).ToList();
        var seen = new HashSet<string>(existing.SelectMany(e => e.Terms.Select(t => GlossaryTermKey.Of(t.Text))));
        var seenTexts = existing.SelectMany(e => e.Terms.Select(t => t.Text)).ToList();
        var room = Math.Max(0, _maxEntries() - existing.Count);
        var rejected = new List<GlossaryImportRejected>();
        var accepted = new List<GlossaryEntry>();
        for (var i = 0; i < file.Entries.Count; i++)
        {
            var e = file.Entries[i];
            var first = e?.Terms?.FirstOrDefault()?.Text ?? "";
            if (e is null) { rejected.Add(new GlossaryImportRejected(i, first, "terms_invalid")); continue; }
            var draft = Clean(new GlossaryEntry
            {
                Kind = e.Kind ?? "",
                Terms = (e.Terms ?? new List<TermJson>()).Select(t => new GlossaryTerm(t.Text ?? "", t.Language ?? "any")).ToList(),
                Entity = e.Entity ?? "",
                Attribute = e.Attribute,
                Conditions = (e.Conditions ?? new List<ConditionJson>()).Select(c => new GlossaryCondition(c.Attribute ?? "", c.Operator ?? "", c.Values ?? new List<string>())).ToList(),
                Description = e.Description ?? "",
                Enabled = e.Enabled,
            });
            var check = await GlossaryValidator.CheckAsync(draft, _metadata, ct);
            if (!check.Ok) { rejected.Add(new GlossaryImportRejected(i, first, check.Reason!)); continue; }
            var keys = draft.Terms.Select(t => GlossaryTermKey.Of(t.Text)).ToList();
            if (keys.Any(seen.Contains) || keys.Distinct().Count() != keys.Count || draft.Terms.Any(t => seenTexts.Any(x => GlossaryMatcher.Equivalent(t.Text, x)))) { rejected.Add(new GlossaryImportRejected(i, first, "duplicate")); continue; }
            if (accepted.Count >= room) { rejected.Add(new GlossaryImportRejected(i, first, "limit")); continue; }
            foreach (var k in keys) { seen.Add(k); }
            seenTexts.AddRange(draft.Terms.Select(t => t.Text));
            accepted.Add(draft);
        }

        if (apply)
        {
            var saved = 0;
            foreach (var draft in accepted)
            {
                var result = await _glossary.AddAsync(draft, actor, ct);
                if (result.Status == GlossarySaveStatus.Saved) { saved++; }
                else { rejected.Add(new GlossaryImportRejected(-1, draft.Terms[0].Text, result.Status == GlossarySaveStatus.Duplicate ? "duplicate" : result.Reason ?? "terms_invalid")); }
            }
            await _audit(actor, "glossary.import", "glossary", $"{saved} of {file.Entries.Count} entries imported", ct);
            return new GlossaryImportReport(file.Entries.Count, saved, rejected, true);
        }
        return new GlossaryImportReport(file.Entries.Count, accepted.Count, rejected, false);
    }
}
