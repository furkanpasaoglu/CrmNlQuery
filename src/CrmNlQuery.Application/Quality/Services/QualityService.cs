using System.Text.Json;
using System.Text.Json.Nodes;
using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Common.Constants;
using CrmNlQuery.Domain.History.Entities;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Application.Quality.Services;

/// <summary>
/// Application service implementing the quality rules.
/// </summary>
public sealed class QualityService
{
    private static readonly string[] DefaultTables = { "account", "contact", "opportunity", "incident", "product", "contract", "salesorder", "lead" };

    private readonly IQualityStore _store;
    private readonly IMetadataSource _metadata;
    private readonly IQueryExecutor _executor;
    private readonly Func<bool> _enabled;
    private readonly Func<int> _maxCases;
    private readonly Func<string, string, string, string, CancellationToken, Task> _audit;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IHistoryStore? _history;
    private readonly Func<bool> _suggestionsAllowed;
    public const int MaxSuggestionsPerUserPerDay = 10;
    public const int MaxPending = 100;
    private readonly object _suggestGate = new object();
    private readonly Dictionary<Guid, (DateOnly Day, int Count)> _suggestedToday = new Dictionary<Guid, (DateOnly, int)>();

    public QualityService(IQualityStore store, IMetadataSource metadata, IQueryExecutor executor, Func<bool> enabled, Func<int> maxCases,
        Func<string, string, string, string, CancellationToken, Task> audit, Func<DateTimeOffset>? clock = null, IHistoryStore? history = null, Func<bool>? suggestionsAllowed = null)
    {
        _history = history;
        _suggestionsAllowed = suggestionsAllowed ?? (() => true);
        _store = store;
        _metadata = metadata;
        _executor = executor;
        _enabled = enabled;
        _maxCases = maxCases;
        _audit = audit;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled => _enabled();
    public bool SuggestionsAllowed => _enabled() && _suggestionsAllowed();
    public bool Persistent => _store.Persistent;
    public int MaxCases => _maxCases();

    /// <summary>
    /// Lists acceptance-set cases matching the given filter.
    /// </summary>
    public Task<IReadOnlyList<QualityCase>> ListAsync(QualityCaseFilter filter, CancellationToken ct = default) => _store.ListAsync(filter, ct);

    /// <summary>
    /// Lists candidate tables for starter-case generation: standard business tables visible in the policy-filtered schema plus custom tables.
    /// </summary>
    public async Task<IReadOnlyList<QualityCandidate>> CandidatesAsync(CancellationToken ct = default)
    {
        var all = await _metadata.ListEntitiesAsync(ct);
        return all.Where(e => DefaultTables.Contains(e.LogicalName) || e.LogicalName.Contains('_'))
            .Select(e => new QualityCandidate(e.LogicalName, e.Label)).OrderBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Generates starter cases for one table from the schema, skipping duplicates of the same question and query; new cases start unverified.
    /// </summary>
    public async Task<QualityGenerateResult> GenerateAsync(string entityName, string language, string actor, Caller caller, CancellationToken ct = default)
    {
        var name = (entityName ?? "").Trim().ToLowerInvariant();
        var info = await _metadata.GetEntityAsync(name, ct);
        if (info is null) { return new QualityGenerateResult(0, 0, "unknown_or_closed"); }

        using var accessScope = AccessLogContext.Begin("other");
        StarterFactsCollector.RunQuery run = (fetch, token) => _executor.ExecuteAsync(info.EntitySetName, fetch, caller, token);
        var count = await StarterFactsCollector.CountRecordsAsync(info, run, null, ct) ?? 0;
        if (count == 0 || string.IsNullOrEmpty(info.PrimaryNameAttribute)) { return new QualityGenerateResult(0, 0, "no_records"); }

        var facts = await StarterFactsCollector.CollectAsync(new[] { info }, new Dictionary<string, int> { [info.LogicalName] = count }, run, null, ct);
        var lang = UiLanguage.Parse(language) ?? UiLanguage.English;
        var generated = StarterSetGenerator.Generate(new[] { info }, facts, lang);

        var room = Math.Max(0, MaxCases - await _store.CountAsync(ct));
        var added = 0;
        var duplicates = 0;
        string? reason = null;
        foreach (var c in generated)
        {
            if (await _store.ExistsAsync(c.Question, c.FetchXml, ct)) { duplicates++; continue; }
            if (added >= room) { reason = "limit_reached"; break; }
            await _store.AddAsync(new QualityCase
            {
                CreatedBy = actor,
                CreatedAt = _clock(),
                Source = QualityCase.SourceGenerated,
                Verified = false,
                Language = lang,
                Question = c.Question,
                Entity = c.Entity,
                AnswersJson = c.AnswerColumns is null ? null : new JsonObject { ["columns"] = c.AnswerColumns }.ToJsonString(),
                ReferenceFetch = c.FetchXml,
                CompareJson = new JsonObject { ["columns"] = c.Columns, ["ties"] = c.Ties }.ToJsonString(),
                Template = c.Template,
                Note = c.Note,
            }, ct);
            added++;
        }
        await _audit(actor, "quality.generate", info.LogicalName, $"{added} added, {duplicates} duplicate(s) skipped", ct);
        return new QualityGenerateResult(added, duplicates, reason);
    }

    /// <summary>
    /// Saves the reviewer's own confirmed answer as a verified case; requires explicit confirmation and rejects chains, summaries, drafts, empty or truncated results, closed data and duplicates.
    /// </summary>
    public async Task<QualitySaveResult> SaveFromResultAsync(Guid owner, string actor, string resultId, QualitySaveRule rule, bool confirmed, Caller caller, string language, CancellationToken ct = default)
    {
        if (_history is null) { return new QualitySaveResult(QualitySaveStatus.NotFound); }
        var order = (rule.Order ?? "").Trim().ToLowerInvariant();
        var columns = (rule.Columns ?? "").Trim().ToLowerInvariant();
        if (!confirmed || (order != "auto" && order != "ordered" && order != "unordered") || (columns != "reference" && columns != "exact"))
        {
            return new QualitySaveResult(QualitySaveStatus.Invalid);
        }

        var checkedResult = await CheckResultAsync(owner, resultId, caller, ct);
        if (checkedResult.Failure is not null) { return checkedResult.Failure; }
        var entry = checkedResult.Entry!;
        var table = checkedResult.Table!;

        var shown = FetchXmlColumns.Of(entry.FetchXml);
        var item = new QualityCase
        {
            CreatedBy = actor,
            CreatedAt = _clock(),
            Source = QualityCase.SourceMarked,
            Verified = true,
            Language = UiLanguage.Parse(language) ?? UiLanguage.English,
            Question = entry.Question,
            Entity = entry.Entity,
            AnswersJson = shown.Count == 0 ? null : new JsonObject { ["columns"] = "custom:" + string.Join(",", shown) }.ToJsonString(),
            ReferenceFetch = entry.FetchXml,
            CompareJson = new JsonObject { ["order"] = order, ["columns"] = columns }.ToJsonString(),
        };
        await _store.AddAsync(item, ct);
        await _audit(actor, "quality.mark", item.Id, table.Rows.Count + " row(s) at the time", ct);
        return new QualitySaveResult(QualitySaveStatus.Saved, Id: item.Id, Rows: table.Rows.Count);
    }

    private sealed record CheckedResult(QualitySaveResult? Failure, HistoryEntry? Entry = null, ResultTable? Table = null);

    private async Task<CheckedResult> CheckResultAsync(Guid owner, string resultId, Caller caller, CancellationToken ct)
    {
        if (_history is null) { return new CheckedResult(new QualitySaveResult(QualitySaveStatus.NotFound)); }
        var entry = await _history.GetAsync(owner, resultId, ct);
        if (entry is null || entry.Kind is not null) { return new CheckedResult(new QualitySaveResult(QualitySaveStatus.NotFound)); }
        if ((entry.Steps ?? 1) > 1) { return new CheckedResult(new QualitySaveResult(QualitySaveStatus.NotSupported, "chain")); }
        if (entry.Question.Contains("(edited:", StringComparison.Ordinal) || entry.Question.Contains("(then:", StringComparison.Ordinal)) { return new CheckedResult(new QualitySaveResult(QualitySaveStatus.NotSupported, "derived")); }

        var info = await _metadata.GetEntityAsync(entry.Entity, ct);
        if (info is null || !await StoredQueryCheck.IsUsableAsync(entry.FetchXml, _metadata, ct)) { return new CheckedResult(new QualitySaveResult(QualitySaveStatus.DataClosed)); }

        using var accessScope = AccessLogContext.Begin("other");
        var table = await _executor.ExecuteAsync(info.EntitySetName, entry.FetchXml, caller, ct);
        if (table.Rows.Count == 0) { return new CheckedResult(new QualitySaveResult(QualitySaveStatus.TooWeak, "empty")); }
        if (AskPipeline.IsTruncated(entry.FetchXml, table.Rows.Count)) { return new CheckedResult(new QualitySaveResult(QualitySaveStatus.TooWeak, "truncated")); }

        if (await _store.ExistsAsync(entry.Question, entry.FetchXml, ct)) { return new CheckedResult(new QualitySaveResult(QualitySaveStatus.Duplicate, Rows: table.Rows.Count)); }
        if (await _store.CountAsync(ct) >= MaxCases) { return new CheckedResult(new QualitySaveResult(QualitySaveStatus.LimitReached)); }
        return new CheckedResult(null, entry, table);
    }

    /// <summary>
    /// Suggests from result for quality service.
    /// </summary>
    public async Task<QualitySaveResult> SuggestFromResultAsync(Guid owner, string resultId, Caller caller, string language, CancellationToken ct = default)
    {
        if (!SuggestionsAllowed) { return new QualitySaveResult(QualitySaveStatus.Disabled); }
        var today = DateOnly.FromDateTime(_clock().UtcDateTime);
        lock (_suggestGate)
        {
            if (_suggestedToday.TryGetValue(owner, out var seen) && seen.Day == today && seen.Count >= MaxSuggestionsPerUserPerDay)
            {
                return new QualitySaveResult(QualitySaveStatus.TooManySuggestions);
            }
        }
        if ((await _store.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusPending), ct)).Count >= MaxPending) { return new QualitySaveResult(QualitySaveStatus.LimitReached); }

        var checkedResult = await CheckResultAsync(owner, resultId, caller, ct);
        if (checkedResult.Failure is not null) { return checkedResult.Failure; }
        var entry = checkedResult.Entry!;
        var table = checkedResult.Table!;

        var shown = FetchXmlColumns.Of(entry.FetchXml);
        var item = new QualityCase
        {
            CreatedBy = "(user suggestion)",
            CreatedAt = _clock(),
            Source = QualityCase.SourceSuggested,
            Status = QualityCase.StatusPending,
            Verified = false,
            Language = UiLanguage.Parse(language) ?? UiLanguage.English,
            Question = entry.Question,
            Entity = entry.Entity,
            AnswersJson = shown.Count == 0 ? null : new JsonObject { ["columns"] = "custom:" + string.Join(",", shown) }.ToJsonString(),
            ReferenceFetch = entry.FetchXml,
            CompareJson = new JsonObject { ["order"] = "auto", ["columns"] = "reference" }.ToJsonString(),
        };
        await _store.AddAsync(item, ct);
        lock (_suggestGate)
        {
            var count = _suggestedToday.TryGetValue(owner, out var seen) && seen.Day == today ? seen.Count : 0;
            _suggestedToday[owner] = (today, count + 1);
        }
        await _audit("(user suggestion)", "quality.suggest", item.Id, table.Rows.Count + " row(s) at the time", ct);
        return new QualitySaveResult(QualitySaveStatus.Saved, Id: item.Id, Rows: table.Rows.Count);
    }

    /// <summary>
    /// Marks the given cases as verified, which also activates pending suggested cases so they join runs; returns how many changed.
    /// </summary>
    public async Task<int> ApproveAsync(IReadOnlyCollection<string> ids, string actor, CancellationToken ct = default)
    {
        var pending = new List<string>();
        foreach (var id in ids)
        {
            if (await _store.GetAsync(id, ct) is { Status: QualityCase.StatusPending }) { pending.Add(id); }
        }
        var n = await _store.SetVerifiedAsync(ids, true, ct);
        if (pending.Count > 0) { await _store.SetStatusAsync(pending, QualityCase.StatusActive, ct); }
        if (n > 0) { await _audit(actor, "quality.approve", n + " case(s)", string.Join(",", ids.Take(20)), ct); }
        return n;
    }

    /// <summary>
    /// Retires the given cases (kept but excluded from runs) or re-activates them, recording the actor, and returns how many changed.
    /// </summary>
    public async Task<int> RetireAsync(IReadOnlyCollection<string> ids, bool retire, string actor, CancellationToken ct = default)
    {
        var n = await _store.SetStatusAsync(ids, retire ? QualityCase.StatusRetired : QualityCase.StatusActive, ct);
        if (n > 0) { await _audit(actor, retire ? "quality.retire" : "quality.restore", n + " case(s)", string.Join(",", ids.Take(20)), ct); }
        return n;
    }

    /// <summary>
    /// Permanently deletes the given acceptance-set cases, recording the actor, and returns how many were removed.
    /// </summary>
    public async Task<int> DeleteAsync(IReadOnlyCollection<string> ids, string actor, CancellationToken ct = default)
    {
        var n = await _store.DeleteAsync(ids, ct);
        if (n > 0) { await _audit(actor, "quality.delete", n + " case(s)", string.Join(",", ids.Take(20)), ct); }
        return n;
    }

    /// <summary>
    /// Exports the active cases in the existing eval case format for the CLI, with no rows or user data.
    /// </summary>
    public async Task<string> ExportAsync(CancellationToken ct = default)
    {
        var cases = await _store.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusActive), ct);
        var array = new JsonArray();
        foreach (var c in cases)
        {
            var compare = string.IsNullOrWhiteSpace(c.CompareJson) ? new JsonObject() : JsonNode.Parse(c.CompareJson)!.AsObject();
            var item = new JsonObject { ["id"] = "q-" + c.Id.Substring(0, Math.Min(8, c.Id.Length)), ["question"] = c.Question };
            if (!string.IsNullOrWhiteSpace(c.AnswersJson)) { item["answers"] = JsonNode.Parse(c.AnswersJson); }
            item["expect"] = new JsonObject
            {
                ["finalKind"] = "answer",
                ["entity"] = c.Entity,
                ["reference"] = new JsonObject { ["fetchXml"] = c.ReferenceFetch, ["compare"] = compare },
            };
            item["generated"] = c.Source == QualityCase.SourceGenerated;
            item["verified"] = c.Verified;
            if (c.Template is not null) { item["template"] = c.Template; }
            if (c.Note is not null) { item["note"] = c.Note; }
            array.Add(item);
        }
        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
