using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Application.DataPolicy.Exceptions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.History.Services;

/// <summary>
/// Saves answers to history owned by their user and re-runs stored queries with the owner's permissions when a result page opens.
/// </summary>
public sealed class ResultService
{
    private readonly IHistoryStore _history;
    private readonly IMetadataSource _metadata;
    private readonly IQueryExecutor _executor;
    private readonly Func<bool>? _adminViewable;

    public ResultService(IHistoryStore history, IMetadataSource metadata, IQueryExecutor executor, Func<bool>? adminViewable = null, CrmNlQuery.Application.Summary.Services.SummaryBuilder? summary = null, CrmNlQuery.Application.Drafts.Services.DraftService? draft = null, Func<bool>? draftEnabled = null)
    {
        _summary = summary;
        _draft = draft;
        _draftEnabled = draftEnabled ?? (() => true);
        _history = history;
        _metadata = metadata;
        _executor = executor;
        _adminViewable = adminViewable;
    }

    /// <summary>
    /// Saves an answer to history bound to its owner, optionally linking the parent result and conversation and the admin-viewable flag.
    /// </summary>
    public async Task<AnswerOutcome> PublishAsync(AnswerOutcome answer, Guid owner, string question, string? parentId = null, string? conversationId = null, CancellationToken ct = default, bool? adminViewable = null)
    {
        var id = await _history.AddAsync(owner, question, answer.Entity, answer.FetchXml, parentId, conversationId, ct, adminViewable: adminViewable ?? _adminViewable?.Invoke() ?? false,
            steps: answer.StepSummaries.Count + 1, durationMs: answer.DurationMs);
        return answer with { ResultId = id, ChatId = conversationId };
    }

    private readonly CrmNlQuery.Application.Summary.Services.SummaryBuilder? _summary;
    private readonly CrmNlQuery.Application.Drafts.Services.DraftService? _draft;
    private readonly Func<bool> _draftEnabled;

    public const string SummaryKind = "summary";
    public const string DraftKind = "draft";
    public const string CreateKind = "create";
    public const string ReportKind = "report";

    private static string LocatorFetchXml(string entity, string recordId)
        => $"<fetch top=\"1\"><entity name=\"{entity}\"><attribute name=\"{entity}id\" /><filter><condition attribute=\"{entity}id\" operator=\"eq\" value=\"{recordId}\" /></filter></entity></fetch>";

    /// <summary>
    /// Writes an email draft to the chat history as a record that stores only the placeholder template, never customer values.
    /// </summary>
    public async Task<DraftOutcome> PublishDraftAsync(DraftOutcome draft, Guid owner, string question, string? conversationId = null, CancellationToken ct = default)
    {
        var payload = new CrmNlQuery.Application.Drafts.Models.DraftPayload(draft.TemplateSubject, draft.TemplateBody, draft.Tone).ToJson();
        var id = await _history.AddAsync(owner, question, draft.Entity, LocatorFetchXml(draft.Entity, draft.RecordId), null, conversationId, ct, adminViewable: _adminViewable?.Invoke() ?? false,
            steps: 1, kind: DraftKind, payload: payload);
        return draft with { ResultId = id, ChatId = conversationId };
    }

    /// <summary>
    /// Writes a report search outcome to the chat as a read-only card holding report ids, names, descriptions and addresses, no customer data.
    /// </summary>
    public async Task<ReportOutcome> PublishReportAsync(ReportOutcome outcome, Guid owner, string question, string? conversationId = null, CancellationToken ct = default)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            listedAll = outcome.ListedAll,
            reports = outcome.Reports.Select(r => new { id = r.Id, name = r.Name, description = r.Description, url = r.Url, forRecord = r.ForRecord }).ToList(),
        });
        var fetch = "<fetch top=\"1\"><entity name=\"report\"><attribute name=\"reportid\" /></entity></fetch>";
        var id = await _history.AddAsync(owner, question, "report", fetch, null, conversationId, ct, adminViewable: _adminViewable?.Invoke() ?? false,
            steps: 1, kind: ReportKind, payload: payload);
        return outcome with { ResultId = id, ChatId = conversationId };
    }

    private static ReportOutcome? ReopenReport(string id, CrmNlQuery.Domain.History.Entities.HistoryEntry entry)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(entry.Payload ?? "");
            var root = document.RootElement;
            var items = new List<CrmNlQuery.Application.Reports.Models.ReportItem>();
            foreach (var r in root.GetProperty("reports").EnumerateArray())
            {
                items.Add(new CrmNlQuery.Application.Reports.Models.ReportItem(r.GetProperty("id").GetString() ?? "", r.GetProperty("name").GetString() ?? "",
                    r.TryGetProperty("description", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String ? d.GetString() : null, r.GetProperty("url").GetString() ?? "")
                {
                    ForRecord = r.TryGetProperty("forRecord", out var fr) && fr.ValueKind == System.Text.Json.JsonValueKind.String ? fr.GetString() : null,
                });
            }
            return new ReportOutcome(entry.Question, items, root.TryGetProperty("listedAll", out var all) && all.ValueKind == System.Text.Json.JsonValueKind.True) { ResultId = id };
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the outcome of a record creation to the chat as a read-only card storing only name, status, record id and error kind, never field values.
    /// </summary>
    public async Task<CrmNlQuery.Application.Create.Models.CreateOutcome> PublishCreateAsync(CrmNlQuery.Application.Create.Models.CreateOutcome outcome, Guid owner, string question, string? conversationId = null, CancellationToken ct = default)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            kind = outcome.Kind,
            entity = outcome.Entity,
            entityLabel = outcome.EntityLabel,
            rows = outcome.Rows.Select(r => new { name = r.Name, status = r.Status, recordId = r.RecordId, error = r.Error, fields = r.FieldNames ?? new List<string>() }).ToList(),
        });
        var fetch = $"<fetch top=\"1\"><entity name=\"{outcome.Entity}\"><attribute name=\"{outcome.Entity}id\" /></entity></fetch>";
        var id = await _history.AddAsync(owner, question, outcome.Entity, fetch, null, conversationId, ct, adminViewable: _adminViewable?.Invoke() ?? false,
            steps: 1, kind: CreateKind, payload: payload);
        return outcome with { ResultId = id, ChatId = conversationId };
    }

    private async Task<CrmNlQuery.Application.Create.Models.CreateOutcome?> ReopenCreateAsync(string id, CrmNlQuery.Domain.History.Entities.HistoryEntry entry, Caller caller, CancellationToken ct)
    {
        CrmNlQuery.Application.Create.Models.CreateOutcome outcome;
        var fieldNames = new List<IReadOnlyList<string>>();
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(entry.Payload ?? "");
            var root = document.RootElement;
            var rows = new List<CrmNlQuery.Application.Create.Models.CreateRowView>();
            foreach (var r in root.GetProperty("rows").EnumerateArray())
            {
                rows.Add(new CrmNlQuery.Application.Create.Models.CreateRowView(
                    r.GetProperty("name").GetString() ?? "", Array.Empty<CrmNlQuery.Application.Create.Models.CreateFieldView>(), Array.Empty<CrmNlQuery.Application.Create.Models.CreateProblemView>(), Array.Empty<string>(),
                    r.GetProperty("status").GetString() ?? "", r.TryGetProperty("recordId", out var rid) && rid.ValueKind == System.Text.Json.JsonValueKind.String ? rid.GetString() : null,
                    r.TryGetProperty("error", out var err) && err.ValueKind == System.Text.Json.JsonValueKind.String ? err.GetString() : null));
                fieldNames.Add(r.TryGetProperty("fields", out var names) && names.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? names.EnumerateArray().Where(n => n.ValueKind == System.Text.Json.JsonValueKind.String).Select(n => n.GetString()!).ToList()
                    : new List<string>());
            }
            outcome = new CrmNlQuery.Application.Create.Models.CreateOutcome(root.GetProperty("kind").GetString() ?? "done", root.GetProperty("entity").GetString() ?? "", root.GetProperty("entityLabel").GetString() ?? "", null, rows) { ResultId = id };
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }

        var filled = new List<CrmNlQuery.Application.Create.Models.CreateRowView>();
        for (var i = 0; i < outcome.Rows.Count; i++)
        {
            var row = outcome.Rows[i];
            if (row.Status == "created" && Guid.TryParse(row.RecordId, out var recordId) && fieldNames[i].Count > 0)
            {
                row = row with { Fields = await ReadCreatedFieldsAsync(outcome.Entity, recordId, fieldNames[i], caller, ct) };
            }
            filled.Add(row);
        }
        return outcome with { Rows = filled };
    }

    private async Task<IReadOnlyList<CrmNlQuery.Application.Create.Models.CreateFieldView>> ReadCreatedFieldsAsync(string entity, Guid recordId, IReadOnlyList<string> attributes, Caller caller, CancellationToken ct)
    {
        try
        {
            var fetch = $"<fetch top=\"1\"><entity name=\"{entity}\">"
                + string.Concat(attributes.Where(a => System.Text.RegularExpressions.Regex.IsMatch(a, "^[a-z0-9_]+$")).Select(a => $"<attribute name=\"{a}\" />"))
                + $"<filter><condition attribute=\"{entity}id\" operator=\"eq\" value=\"{recordId}\" /></filter></entity></fetch>";
            var answer = await ExecuteStoredAsync(entity, fetch, caller, ct);
            if (answer is null || answer.Table.Rows.Count == 0)
            {
                return Array.Empty<CrmNlQuery.Application.Create.Models.CreateFieldView>();
            }
            var labels = await CrmNlQuery.Application.Explain.Services.ColumnLabeler.TryLabelAsync(fetch, answer.Table.Columns, _metadata, ct) ?? answer.Table.Columns;
            var row = answer.Table.Rows[0];
            var fields = new List<CrmNlQuery.Application.Create.Models.CreateFieldView>();
            for (var c = 0; c < row.Count && c < labels.Count; c++)
            {
                if (!string.IsNullOrEmpty(row[c]))
                {
                    fields.Add(new CrmNlQuery.Application.Create.Models.CreateFieldView(labels[c], row[c]));
                }
            }
            return fields;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Array.Empty<CrmNlQuery.Application.Create.Models.CreateFieldView>();
        }
    }

    /// <summary>
    /// Writes a customer summary to history, storing only a query that finds the root record so the summary is recomputed on open.
    /// </summary>
    public async Task<SummaryOutcome> PublishSummaryAsync(SummaryOutcome summary, Guid owner, string question, string? conversationId = null, CancellationToken ct = default)
    {
        var fetchXml = LocatorFetchXml(summary.Entity, summary.RecordId);
        var id = await _history.AddAsync(owner, question, summary.Entity, fetchXml, null, conversationId, ct, adminViewable: _adminViewable?.Invoke() ?? false,
            steps: 1, kind: SummaryKind);
        return summary with { ResultId = id, ChatId = conversationId };
    }

    /// <summary>
    /// Checks whether the given history entry is a customer summary owned by this caller.
    /// </summary>
    public async Task<bool> IsSummaryAsync(string id, Guid caller, CancellationToken ct = default)
        => (await _history.GetAsync(caller, id, ct))?.Kind is SummaryKind or DraftKind or CreateKind;

    /// <summary>
    /// Opens a history entry for its owner, recomputing a summary with the caller's permissions or returning the query answer; returns null if unknown or not theirs.
    /// </summary>
    public async Task<AskOutcome?> OpenAnyAsync(string id, Guid caller, CancellationToken ct = default)
    {
        var entry = await _history.GetAsync(caller, id, ct);
        if (entry is null)
        {
            return null;
        }
        if (entry.Kind == ReportKind)
        {
            return ReopenReport(id, entry);
        }
        if (entry.Kind == CreateKind)
        {
            using var createScope = CrmNlQuery.Application.Observability.AccessLog.AccessLogContext.Begin("result");
            return await ReopenCreateAsync(id, entry, Caller.User(caller), ct);
        }
        if (entry.Kind == DraftKind)
        {
            var payload = CrmNlQuery.Application.Drafts.Models.DraftPayload.Parse(entry.Payload);
            var draftRecord = CrmNlQuery.Application.Summary.Services.SummaryRecord.IdOf(entry.FetchXml);
            if (_draft is null || !_draftEnabled() || payload is null || draftRecord is null)
            {
                return null;
            }
            using var draftScope = CrmNlQuery.Application.Observability.AccessLog.AccessLogContext.Begin("draft");
            var refilled = await _draft.RefillAsync(Caller.User(caller), entry.Entity, draftRecord.Value, payload, ct);
            return refilled with { ResultId = id };
        }
        if (entry.Kind != SummaryKind)
        {
            return await OpenAsync(id, caller, ct);
        }
        var recordId = CrmNlQuery.Application.Summary.Services.SummaryRecord.IdOf(entry.FetchXml);
        if (_summary is null || recordId is null)
        {
            return null;
        }
        using var accessScope = CrmNlQuery.Application.Observability.AccessLog.AccessLogContext.Begin("result");
        var summary = await _summary.BuildForRecordAsync(Caller.User(caller), entry.Entity, recordId.Value, ct);
        return summary with { ResultId = id };
    }

    /// <summary>
    /// Re-runs a stored result for its owner with their permissions; returns null if not the owner, unknown, or the table is gone.
    /// </summary>
    public async Task<AnswerOutcome?> OpenAsync(string id, Guid caller, CancellationToken ct = default)
    {
        using var accessScope = CrmNlQuery.Application.Observability.AccessLog.AccessLogContext.Begin("result");
        var entry = await _history.GetAsync(caller, id, ct);
        if (entry is null)
        {
            return null;
        }
        var answer = await ExecuteStoredAsync(entry.Entity, entry.FetchXml, Caller.User(caller), ct);
        return answer is null ? null : answer with { ResultId = id };
    }

    /// <summary>
    /// Runs a stored or edited query as the given caller, returning null if the table no longer exists and throwing if it now uses closed data.
    /// </summary>
    public async Task<AnswerOutcome?> ExecuteStoredAsync(string entityName, string fetchXml, Caller caller, CancellationToken ct = default)
    {
        var entity = await _metadata.GetEntityAsync(entityName, ct);
        if (entity is null)
        {
            return null;
        }
        if (!await StoredQueryCheck.IsUsableAsync(fetchXml, _metadata, ct))
        {
            throw new DataPolicyException();
        }

        var table = await _executor.ExecuteAsync(entity.EntitySetName, fetchXml, caller, ct);
        var truncated = AskPipeline.IsTruncated(fetchXml, table.Rows.Count);
        return new AnswerOutcome(entityName, "", fetchXml, table, truncated)
        {
            NarrowHints = truncated ? NarrowHintBuilder.For(fetchXml, entity) : Array.Empty<NarrowHint>(),
        };
    }
}
