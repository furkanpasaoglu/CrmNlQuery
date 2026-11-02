using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Models;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.DataPolicy.Services;

/// <summary>
/// Provides the admin panel logic for the Data access page, listing tables from the raw schema and saving decisions to the store.
/// </summary>
public sealed class DataPolicyAdminService
{
    private readonly IMetadataSource _raw;
    private readonly IDataPolicyStore _store;
    private readonly DataPolicyService _policy;
    private readonly IQueryExecutor _executor;
    private readonly Func<string, string, string, string, CancellationToken, Task> _audit;
    private readonly Func<DateTimeOffset> _clock;

    public DataPolicyAdminService(IMetadataSource raw, IDataPolicyStore store, DataPolicyService policy, IQueryExecutor executor,
        Func<string, string, string, string, CancellationToken, Task> audit, Func<DateTimeOffset>? clock = null)
    {
        _raw = raw;
        _store = store;
        _policy = policy;
        _executor = executor;
        _audit = audit;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private static string Source(TableState state) => state switch
    {
        TableState.Open => "starting list",
        TableState.OpenByAdmin => "opened by an administrator",
        TableState.ClosedByAdmin => "closed by an administrator",
        _ => "closed by default",
    };

    private static string Describe(FieldState state) => state switch
    {
        FieldState.Open => "open",
        FieldState.OpenByAdmin => "opened by an administrator",
        FieldState.ClosedByAdmin => "closed by an administrator",
        _ => "closed automatically",
    };

    private static bool IsLocked(EntityInfo entity, string attribute)
        => string.Equals(attribute, entity.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase)
           || string.Equals(attribute, entity.PrimaryNameAttribute, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lists tables from the raw unfiltered schema with their state and source, optionally filtered by a search text.
    /// </summary>
    public async Task<IReadOnlyList<DataPolicyTableRow>> ListTablesAsync(string? search, CancellationToken ct = default)
    {
        var snapshot = await _policy.GetAsync(ct);
        var term = (search ?? "").Trim();
        var rows = new List<DataPolicyTableRow>();
        foreach (var summary in await _raw.ListEntitiesAsync(ct))
        {
            if (term.Length > 0
                && summary.LogicalName.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0
                && summary.Label.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            var state = snapshot.StateOfTable(summary.LogicalName);
            var open = state == TableState.Open || state == TableState.OpenByAdmin;
            int? auto = null;
            int? admin = null;
            if (open && await _raw.GetEntityAsync(summary.LogicalName, ct) is { } entity)
            {
                var states = entity.Attributes.Where(a => !IsLocked(entity, a.LogicalName)).Select(a => snapshot.StateOfField(entity.LogicalName, a.LogicalName, a.Label)).ToList();
                auto = states.Count(s => s == FieldState.ClosedAutomatically);
                admin = states.Count(s => s == FieldState.ClosedByAdmin);
            }
            rows.Add(new DataPolicyTableRow(summary.LogicalName, summary.Label, open, Source(state), auto, admin));
        }
        return rows.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Lists the fields of a table with their state and lock flags, or null when the table does not exist.
    /// </summary>
    public async Task<IReadOnlyList<DataPolicyFieldRow>?> ListFieldsAsync(string entityName, CancellationToken ct = default)
    {
        var entity = await _raw.GetEntityAsync(entityName, ct);
        if (entity is null)
        {
            return null;
        }
        var snapshot = await _policy.GetAsync(ct);
        return entity.Attributes
            .Select(a =>
            {
                var locked = IsLocked(entity, a.LogicalName);
                var state = locked ? FieldState.Open : snapshot.StateOfField(entity.LogicalName, a.LogicalName, a.Label);
                return new DataPolicyFieldRow(a.LogicalName, a.Label, a.AttributeType, state == FieldState.Open || state == FieldState.OpenByAdmin, Describe(state), locked);
            })
            .OrderBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Lists fields automatically closed by the sensitive-name rule in open tables so administrators can review them.
    /// </summary>
    public async Task<IReadOnlyList<DataPolicyReviewRow>> ReviewAsync(CancellationToken ct = default)
    {
        var snapshot = await _policy.GetAsync(ct);
        var rows = new List<DataPolicyReviewRow>();
        foreach (var summary in await _raw.ListEntitiesAsync(ct))
        {
            if (!snapshot.IsTableOpen(summary.LogicalName) || await _raw.GetEntityAsync(summary.LogicalName, ct) is not { } entity)
            {
                continue;
            }
            foreach (var a in entity.Attributes.Where(a => !IsLocked(entity, a.LogicalName)))
            {
                if (snapshot.StateOfField(entity.LogicalName, a.LogicalName, a.Label) == FieldState.ClosedAutomatically)
                {
                    rows.Add(new DataPolicyReviewRow(entity.LogicalName, entity.Label, a.LogicalName, a.Label));
                }
            }
        }
        return rows;
    }

    /// <summary>
    /// Opens, closes or resets a table decision for an administrator, refusing to open it unless the CRM read check succeeds.
    /// </summary>
    public async Task<DataPolicyResult> SetTableAsync(string actor, string entityName, bool? allowed, Caller caller, CancellationToken ct = default)
    {
        var entity = await _raw.GetEntityAsync((entityName ?? "").Trim().ToLowerInvariant(), ct);
        if (entity is null)
        {
            return DataPolicyResult.Fail(MessageCatalog.Now(Msg.AdmUnknownTable));
        }
        var before = (await _policy.GetAsync(ct)).StateOfTable(entity.LogicalName);

        if (allowed == true)
        {
            var probe = await CheckAccessAsync(entity.LogicalName, caller, ct);
            if (probe.Outcome != "readable")
            {
                return DataPolicyResult.Fail(probe.Outcome == "error" ? MessageCatalog.Now(Msg.AdmCouldNotVerify, MessageCatalog.Args(("message", probe.Message))) : probe.Message);
            }
        }

        if (allowed is null)
        {
            await _store.RemoveAsync(DataPolicyRule.Table, entity.LogicalName, null, ct);
        }
        else
        {
            await _store.SetAsync(new DataPolicyRule(DataPolicyRule.Table, entity.LogicalName, null, allowed.Value, actor, _clock()), ct);
        }
        _policy.Invalidate();
        var after = allowed is null ? "reset to default" : allowed.Value ? "open" : "closed";
        await _audit(actor, "datapolicy.table", entity.LogicalName, Source(before) + " → " + after, ct);
        return DataPolicyResult.Success;
    }

    /// <summary>
    /// Opens or closes a field for an administrator, keeping the primary id and name fields locked.
    /// </summary>
    public async Task<DataPolicyResult> SetFieldAsync(string actor, string entityName, string attribute, bool? allowed, CancellationToken ct = default)
    {
        var entity = await _raw.GetEntityAsync((entityName ?? "").Trim().ToLowerInvariant(), ct);
        var info = entity?.FindAttribute((attribute ?? "").Trim());
        if (entity is null || info is null)
        {
            return DataPolicyResult.Fail(MessageCatalog.Now(Msg.AdmUnknownTableOrField));
        }
        if (IsLocked(entity, info.LogicalName))
        {
            return DataPolicyResult.Fail(MessageCatalog.Now(Msg.AdmLockedField));
        }
        var before = (await _policy.GetAsync(ct)).StateOfField(entity.LogicalName, info.LogicalName, info.Label);

        if (allowed is null)
        {
            await _store.RemoveAsync(DataPolicyRule.Field, entity.LogicalName, info.LogicalName, ct);
        }
        else
        {
            await _store.SetAsync(new DataPolicyRule(DataPolicyRule.Field, entity.LogicalName, info.LogicalName, allowed.Value, actor, _clock()), ct);
        }
        _policy.Invalidate();
        var after = allowed is null ? "reset to default" : allowed.Value ? "open" : "closed";
        await _audit(actor, "datapolicy.field", entity.LogicalName + "." + info.LogicalName, Describe(before) + " → " + after, ct);
        return DataPolicyResult.Success;
    }

    /// <summary>
    /// Reads a single primary-id row as the administrator to check readability without returning any row data.
    /// </summary>
    public async Task<DataPolicyProbeResult> CheckAccessAsync(string entityName, Caller caller, CancellationToken ct = default)
    {
        var entity = await _raw.GetEntityAsync((entityName ?? "").Trim().ToLowerInvariant(), ct);
        if (entity is null)
        {
            return new DataPolicyProbeResult("error", MessageCatalog.Now(Msg.AdmUnknownTable));
        }
        var fetch = $"<fetch top=\"1\"><entity name=\"{entity.LogicalName}\"><attribute name=\"{entity.PrimaryIdAttribute}\" /></entity></fetch>";
        using var accessScope = AccessLogContext.Begin("other");
        try
        {
            await _executor.ExecuteAsync(entity.EntitySetName, fetch, caller, ct);
            return new DataPolicyProbeResult("readable", MessageCatalog.Now(Msg.AdmProbeReadable));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CrmException ex) when (ex.IsPermissionDenied)
        {
            return new DataPolicyProbeResult("not_readable", MessageCatalog.Now(Msg.AdmProbeNotReadable, new Dictionary<string, string> { ["table"] = entity.LogicalName }));
        }
        catch (Exception)
        {
            return new DataPolicyProbeResult("error", MessageCatalog.Now(Msg.AdmProbeError));
        }
    }
}
