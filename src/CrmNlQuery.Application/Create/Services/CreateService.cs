using System.Security;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Create.Abstractions;
using CrmNlQuery.Application.Create.Models;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Create.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Create.Services;

/// <summary>
/// Handles record creation from chat with a human in the loop: rule-based intent, model proposal, code validation, preview and the user's explicit confirmation.
/// </summary>
public sealed class CreateService
{
    private readonly ICreateSchemaSource _schemas;
    private readonly CreateExtractor _extractor;
    private readonly IRecordSearcher _searcher;
    private readonly IMetadataSource _metadata;
    private readonly IRecordWriter _writer;
    private readonly IQueryExecutor _executor;
    private readonly ICreateDraftStore _store;
    private readonly IDataPolicy? _policy;
    private readonly Func<AuditEntry, CancellationToken, Task> _audit;
    private readonly Func<bool> _enabled;
    private readonly Func<IReadOnlyList<string>> _tables;
    private readonly Func<int> _maxRows;
    private readonly Func<int> _perDay;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _quotaGate = new object();
    private readonly Dictionary<(Guid, DateOnly), int> _today = new Dictionary<(Guid, DateOnly), int>();

    public CreateService(ICreateSchemaSource schemas, CreateExtractor extractor, IRecordSearcher searcher, IMetadataSource metadata, IRecordWriter writer,
        IQueryExecutor executor, ICreateDraftStore store, IDataPolicy? policy, Func<AuditEntry, CancellationToken, Task> audit,
        Func<bool> enabled, Func<IReadOnlyList<string>> tables, Func<int> maxRows, Func<int> perDay, Func<DateTimeOffset>? clock = null)
    {
        _schemas = schemas;
        _extractor = extractor;
        _searcher = searcher;
        _metadata = metadata;
        _writer = writer;
        _executor = executor;
        _store = store;
        _policy = policy;
        _audit = audit;
        _enabled = enabled;
        _tables = tables;
        _maxRows = maxRows;
        _perDay = perDay;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled => _enabled() && _tables().Count > 0;

    /// <summary>
    /// Checks cheaply and without a model whether a message could be a creation request or an answer to a pending draft.
    /// </summary>
    public async Task<bool> IsCandidateAsync(string question, Guid? draftId, Caller caller, CancellationToken ct = default)
    {
        if (!Enabled || caller.IsEvaluation)
        {
            return false;
        }
        if (draftId is not null && _store.Get(draftId.Value, caller.UserId) is not null)
        {
            return true;
        }
        var schemas = await LoadSchemasAsync(ct);
        if (schemas.Count == 0)
        {
            return false;
        }
        return CreateIntent.Matches(question, schemas) || IsJsonRequest(question, schemas);
    }

    private static bool IsJsonRequest(string question, IReadOnlyList<CreateSchema> schemas)
    {
        var result = CreateJsonInput.TryParse(question, schemas, out var json);
        if (result == CreateJsonResult.NotJson)
        {
            return false;
        }
        return CreateIntent.HasVerb(question) || (json?.Entity is { } named && schemas.Any(s => CreateIntent.Names(s, named)));
    }

    /// <summary>
    /// Gates the start of the ask flow, returning null when the message is not a creation request so the normal path continues.
    /// </summary>
    public async Task<AskOutcome?> HandleAsync(string question, Guid? draftId, Caller caller, bool canCreate, string actor = "", CancellationToken ct = default)
    {
        if (!Enabled || caller.IsEvaluation)
        {
            return null;
        }
        var schemas = await LoadSchemasAsync(ct);
        if (schemas.Count == 0)
        {
            return null;
        }

        if (draftId is not null && _store.Get(draftId.Value, caller.UserId) is { } pending)
        {
            return canCreate ? await ContinueAsync(pending, question, schemas, caller, ct) : await DeniedAsync(actor, "", "no_permission", ct);
        }
        if (IsJsonRequest(question, schemas))
        {
            return canCreate ? await StartFromJsonAsync(question, schemas, caller, actor, ct) : await DeniedAsync(actor, "", "no_permission", ct);
        }
        if (!CreateIntent.Matches(question, schemas))
        {
            return null;
        }
        if (!canCreate)
        {
            return await DeniedAsync(actor, "", "no_permission", ct);
        }
        return await StartAsync(question, schemas, caller, actor, ct);
    }

    private static FailureOutcome Forbidden() => FailureOutcome.FromKey(Msg.CreateForbidden, "create_forbidden");

    private readonly Dictionary<(string, string), DateTimeOffset> _deniedSeen = new Dictionary<(string, string), DateTimeOffset>();

    private async Task<FailureOutcome> DeniedAsync(string actor, string entity, string reason, CancellationToken ct)
    {
        var refused = reason == "no_permission" ? Forbidden() : reason == "table_closed" ? Forbidden() : reason == "daily_limit"
            ? FailureOutcome.FromKey(Msg.CreateDailyLimit, "create_limit", MessageCatalog.Args(("max", _perDay().ToString())))
            : Forbidden();
        if (string.IsNullOrWhiteSpace(actor))
        {
            return refused;
        }
        var now = _clock();
        lock (_deniedSeen)
        {
            if (_deniedSeen.TryGetValue((actor, reason), out var last) && now - last < TimeSpan.FromMinutes(10))
            {
                return refused;
            }
            _deniedSeen[(actor, reason)] = now;
        }
        try
        {
            await _audit(new AuditEntry(now, actor, "record.create.denied", entity.Length > 0 ? entity : "(chat)", reason), ct);
        }
        catch (Exception)
        {
        }
        return refused;
    }

    private async Task<AskOutcome> StartAsync(string question, IReadOnlyList<CreateSchema> schemas, Caller caller, string actor, CancellationToken ct)
    {
        var extraction = await _extractor.ExtractAsync(question, schemas, null, ct);
        if (extraction is null)
        {
            return FailureOutcome.FromKey(Msg.CreateNotUnderstood, "create_failed");
        }
        var schema = schemas.FirstOrDefault(s => string.Equals(s.Entity, extraction.Entity, StringComparison.OrdinalIgnoreCase));
        if (schema is null)
        {
            return FailureOutcome.FromKey(Msg.CreateWhichTable, "create_table", MessageCatalog.Args(("tables", string.Join(", ", schemas.Select(s => s.Label)))));
        }
        var rows = extraction.Records.Count == 0 ? new List<IReadOnlyDictionary<string, string>> { new Dictionary<string, string>() } : extraction.Records.ToList();
        return await StartDraftAsync(question, schema, rows, caller, actor, ct);
    }

    private async Task<AskOutcome> StartFromJsonAsync(string question, IReadOnlyList<CreateSchema> schemas, Caller caller, string actor, CancellationToken ct)
    {
        var result = CreateJsonInput.TryParse(question, schemas, out var json);
        if (result != CreateJsonResult.Ok || json is null)
        {
            return FailureOutcome.FromKey(Msg.CreateJsonShape, "create_failed");
        }
        CreateSchema? schema;
        if (json.Entity is { } named)
        {
            schema = schemas.FirstOrDefault(s => CreateIntent.Names(s, named));
        }
        else
        {
            var mentioned = CreateIntent.Mentioned(question, schemas);
            schema = mentioned.Count == 1 ? mentioned[0] : schemas.Count == 1 ? schemas[0] : null;
        }
        if (schema is null)
        {
            return FailureOutcome.FromKey(Msg.CreateWhichTable, "create_table", MessageCatalog.Args(("tables", string.Join(", ", schemas.Select(s => s.Label)))));
        }
        return await StartDraftAsync(question, schema, json.Records.ToList(), caller, actor, ct);
    }

    private async Task<AskOutcome> StartDraftAsync(string question, CreateSchema schema, List<IReadOnlyDictionary<string, string>> rows, Caller caller, string actor, CancellationToken ct)
    {
        if (rows.Count > _maxRows())
        {
            await DeniedAsync(actor, schema.Entity, "too_many_rows", ct);
            return FailureOutcome.FromKey(Msg.CreateTooMany, "create_limit", MessageCatalog.Args(("max", _maxRows().ToString())));
        }
        var draft = new CreateDraft(Guid.NewGuid(), caller.UserId, schema.Entity, question, rows, _clock());
        return await BuildAsync(draft, schema, caller, ct);
    }

    private async Task<AskOutcome?> ContinueAsync(CreateDraft draft, string text, IReadOnlyList<CreateSchema> schemas, Caller caller, CancellationToken ct)
    {
        var schema = schemas.FirstOrDefault(s => string.Equals(s.Entity, draft.Entity, StringComparison.OrdinalIgnoreCase));
        if (schema is null)
        {
            _store.Remove(draft.Id);
            return null;
        }
        if (draft.Rows.Count != 1)
        {
            return null;
        }
        var extraction = await _extractor.ExtractAsync(text, new[] { schema }, draft.Rows[0], ct);
        var added = extraction?.Records.FirstOrDefault();
        if (added is null || added.Count == 0)
        {
            return null;
        }
        var merged = new Dictionary<string, string>(draft.Rows[0], StringComparer.OrdinalIgnoreCase);
        foreach (var pair in added)
        {
            merged[pair.Key] = pair.Value;
        }
        return await BuildAsync(draft.WithRows(new List<IReadOnlyDictionary<string, string>> { merged }), schema, caller, ct);
    }

    private async Task<CreateOutcome> BuildAsync(CreateDraft draft, CreateSchema schema, Caller caller, CancellationToken ct)
    {
        var validator = NewValidator(caller);
        var views = new List<CreateRowView>();
        var normalized = new List<IReadOnlyDictionary<string, string>>();
        var checkedDuplicates = 0;
        foreach (var raw in draft.Rows)
        {
            var row = await validator.ValidateAsync(schema, raw, ct);
            normalized.Add(MergeRaw(raw, row));
            var duplicates = Array.Empty<string>() as IReadOnlyList<string>;
            if (row.IsComplete && row.DisplayName.Length > 0 && checkedDuplicates++ < 20)
            {
                duplicates = await FindDuplicatesAsync(schema, row.DisplayName, caller, ct);
            }
            views.Add(ToView(schema, row, duplicates));
        }
        var saved = draft.WithRows(normalized);
        _store.Save(saved);
        var anyProblem = views.Any(v => v.Status == "needs_info");
        var kind = anyProblem && views.Count == 1 ? "ask" : "preview";
        return new CreateOutcome(kind, schema.Entity, schema.Label, saved.Id.ToString("N"), views) { Question = draft.Question };
    }

    private static IReadOnlyDictionary<string, string> MergeRaw(IReadOnlyDictionary<string, string> original, CreateRow row)
    {
        var merged = new Dictionary<string, string>(row.Raw, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in original)
        {
            if (row.Problems.Any(p => p.Code == "unknown_field" && string.Equals(p.Attribute, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                merged[pair.Key] = pair.Value;
            }
        }
        return merged;
    }

    private static CreateRowView ToView(CreateSchema schema, CreateRow row, IReadOnlyList<string> duplicates)
    {
        var fields = row.Values.Select(v => new CreateFieldView(v.Label, v.Display)).ToList();
        var problems = row.Problems.Select(p => new CreateProblemView(p.Code, p.Label, p.Detail)).ToList();
        var name = row.DisplayName.Length > 0 ? row.DisplayName : schema.Label;
        return new CreateRowView(name, fields, problems, duplicates, row.IsComplete ? "ready" : "needs_info", FieldNames: row.Values.Select(v => v.Attribute).ToList());
    }

    private CreateValidator NewValidator(Caller caller) => new CreateValidator((entity, text, token) => SearchLookupAsync(entity, text, caller, token));

    private async Task<IReadOnlyList<RecordMatch>> SearchLookupAsync(string entity, string text, Caller caller, CancellationToken ct)
    {
        var info = await _metadata.GetEntityAsync(entity, ct);
        if (info is null)
        {
            return Array.Empty<RecordMatch>();
        }
        try
        {
            return await _searcher.SearchByNameAsync(info, text, caller, ct);
        }
        catch (CrmException)
        {
            return Array.Empty<RecordMatch>();
        }
    }

    private async Task<IReadOnlyList<string>> FindDuplicatesAsync(CreateSchema schema, string name, Caller caller, CancellationToken ct)
    {
        try
        {
            var fetch = $"<fetch top=\"3\"><entity name=\"{schema.Entity}\"><attribute name=\"{schema.PrimaryNameAttribute}\" /><filter><condition attribute=\"{schema.PrimaryNameAttribute}\" operator=\"eq\" value=\"{SecurityElement.Escape(name)}\" /></filter></entity></fetch>";
            var table = await _executor.ExecuteAsync(schema.EntitySet, fetch, caller, ct);
            return table.Rows.Where(r => r.Count > 0).Select(r => r[0]).ToList();
        }
        catch (CrmException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Confirms a draft by re-validating the rows and writing the valid ones as the caller, after checking duplicates, quota and ownership.
    /// </summary>
    public async Task<AskOutcome> ConfirmAsync(Guid draftId, Caller caller, string actorAccount, bool allowDuplicates, bool canCreate, CancellationToken ct = default)
    {
        if (!Enabled || caller.IsEvaluation || !canCreate)
        {
            return Enabled && !caller.IsEvaluation ? await DeniedAsync(actorAccount, "", "no_permission", ct) : Forbidden();
        }
        var draft = _store.Get(draftId, caller.UserId);
        if (draft is null)
        {
            return FailureOutcome.FromKey(Msg.CreateDraftGone, "create_gone");
        }
        var schemas = await LoadSchemasAsync(ct);
        var schema = schemas.FirstOrDefault(s => string.Equals(s.Entity, draft.Entity, StringComparison.OrdinalIgnoreCase));
        if (schema is null)
        {
            _store.Remove(draft.Id);
            return await DeniedAsync(actorAccount, draft.Entity, "table_closed", ct);
        }

        var validator = NewValidator(caller);
        var rows = new List<(CreateRow Row, IReadOnlyList<string> Duplicates)>();
        foreach (var raw in draft.Rows)
        {
            var row = await validator.ValidateAsync(schema, raw, ct);
            var duplicates = row.IsComplete && row.DisplayName.Length > 0 && rows.Count < 20 ? await FindDuplicatesAsync(schema, row.DisplayName, caller, ct) : Array.Empty<string>();
            rows.Add((row, duplicates));
        }
        var writable = rows.Where(r => r.Row.IsComplete).ToList();
        if (writable.Count == 0)
        {
            return await BuildAsync(draft, schema, caller, ct);
        }
        if (!allowDuplicates && writable.Any(r => r.Duplicates.Count > 0))
        {
            return await BuildAsync(draft, schema, caller, ct);
        }
        if (!TryReserve(caller.UserId, writable.Count))
        {
            await DeniedAsync(actorAccount, schema.Entity, "daily_limit", ct);
            return FailureOutcome.FromKey(Msg.CreateDailyLimit, "create_limit", MessageCatalog.Args(("max", _perDay().ToString())));
        }

        var views = new List<CreateRowView>();
        var createdIds = new List<string>();
        var failed = 0;
        foreach (var (row, duplicates) in rows)
        {
            if (!row.IsComplete)
            {
                views.Add(ToView(schema, row, duplicates) with { Status = "skipped" });
                continue;
            }
            try
            {
                var id = await _writer.CreateAsync(schema, row.ToPayload(), caller, ct);
                createdIds.Add(id.ToString("N"));
                views.Add(ToView(schema, row, duplicates) with { Status = "created", RecordId = id.ToString("N") });
            }
            catch (CrmException ex)
            {
                failed++;
                views.Add(ToView(schema, row, duplicates) with { Status = "failed", Error = ex.IsPermissionDenied || ex.IsImpersonationDenied ? "permission" : "crm" });
            }
        }
        Release(caller.UserId, failed);

        await _audit(new AuditEntry(_clock(), actorAccount, "record.create", schema.Entity,
            $"created {createdIds.Count}, failed {failed}, skipped {rows.Count - writable.Count}; ids: {string.Join(",", createdIds.Take(50))}"), ct);
        _store.Remove(draft.Id);
        return new CreateOutcome("done", schema.Entity, schema.Label, null, views) { Question = draft.Question };
    }

    /// <summary>
    /// Cancels a pending draft owned by the caller.
    /// </summary>
    public CreateOutcome Cancel(Guid draftId, Caller caller)
    {
        var draft = _store.Get(draftId, caller.UserId);
        _store.Remove(draftId);
        return new CreateOutcome("cancelled", draft?.Entity ?? "", draft?.Entity ?? "", null, Array.Empty<CreateRowView>()) { Question = draft?.Question };
    }

    private async Task<IReadOnlyList<CreateSchema>> LoadSchemasAsync(CancellationToken ct)
    {
        var snapshot = _policy is null ? null : await _policy.GetAsync(ct);
        var result = new List<CreateSchema>();
        foreach (var entity in _tables())
        {
            if (snapshot is { Enabled: true } && !snapshot.IsTableOpen(entity))
            {
                continue;
            }
            var schema = await _schemas.GetAsync(entity, ct);
            if (schema is null)
            {
                continue;
            }
            if (snapshot is { Enabled: true })
            {
                var open = schema.Fields.Where(f => snapshot.IsFieldOpen(schema.Entity, f.Name, f.Label)).ToList();
                if (schema.Fields.Any(f => f.Required && !open.Contains(f)))
                {
                    continue;
                }
                schema = schema with { Fields = open };
            }
            result.Add(schema);
        }
        return result;
    }

    private bool TryReserve(Guid user, int count)
    {
        lock (_quotaGate)
        {
            var key = (user, DateOnly.FromDateTime(_clock().UtcDateTime));
            foreach (var old in _today.Keys.Where(k => k.Item2 != key.Item2).ToList())
            {
                _today.Remove(old);
            }
            _today.TryGetValue(key, out var used);
            if (used + count > _perDay())
            {
                return false;
            }
            _today[key] = used + count;
            return true;
        }
    }

    private void Release(Guid user, int count)
    {
        if (count <= 0)
        {
            return;
        }
        lock (_quotaGate)
        {
            var key = (user, DateOnly.FromDateTime(_clock().UtcDateTime));
            if (_today.TryGetValue(key, out var used))
            {
                _today[key] = Math.Max(0, used - count);
            }
        }
    }
}
