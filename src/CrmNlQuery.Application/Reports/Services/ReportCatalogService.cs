using System.Text.RegularExpressions;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Domain.Reports.Entities;

namespace CrmNlQuery.Application.Reports.Services;

/// <summary>
/// Manages the report catalog with add, update, delete, validation and audit, without recording report data.
/// </summary>
public sealed class ReportCatalogService
{
    public const int MaxKeywords = 20;
    private static readonly Regex EntityName = new Regex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex ParamName = new Regex("^[A-Za-z0-9_.:-]{1,60}$", RegexOptions.Compiled);

    private readonly IReportCatalogStore _store;
    private readonly Func<int> _maxEntries;
    private readonly Func<string, string, string, string, CancellationToken, Task> _audit;
    private readonly Func<DateTimeOffset> _clock;

    public ReportCatalogService(IReportCatalogStore store, Func<int> maxEntries, Func<string, string, string, string, CancellationToken, Task> audit, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _maxEntries = maxEntries;
        _audit = audit;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Persistent => _store.Persistent;
    public int MaxEntries => _maxEntries();

    /// <summary>
    /// Returns all report catalog entries sorted by name using culture-aware, case-insensitive comparison.
    /// </summary>
    public async Task<IReadOnlyList<ReportCatalogEntry>> ListAsync(CancellationToken ct = default)
        => (await _store.ListAsync(ct)).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// Validates and saves a report catalog entry and writes an audit record.
    /// </summary>
    public async Task<ReportCatalogResult> SaveAsync(string actor, ReportCatalogInput input, CancellationToken ct = default)
    {
        var existing = string.IsNullOrWhiteSpace(input.Id) ? null : await _store.GetAsync(input.Id, ct);
        if (!string.IsNullOrWhiteSpace(input.Id) && existing is null) { return new ReportCatalogResult("not_found"); }
        var (error, clean) = Validate(input);
        if (error is not null) { return new ReportCatalogResult(error); }
        if (existing is null && (await _store.ListAsync(ct)).Count >= MaxEntries) { return new ReportCatalogResult("limit"); }

        var entry = clean! with { Id = existing?.Id ?? Guid.NewGuid().ToString("N"), UpdatedBy = actor, UpdatedAt = _clock() };
        await _store.UpsertAsync(entry, ct);
        await _audit(actor, existing is null ? "reports.create" : "reports.update", entry.Id, entry.Name, ct);
        return new ReportCatalogResult(null, entry);
    }

    /// <summary>
    /// Deletes a report catalog entry and writes an audit record.
    /// </summary>
    public async Task<bool> DeleteAsync(string actor, string id, CancellationToken ct = default)
    {
        var existing = await _store.GetAsync(id, ct);
        if (existing is null || !await _store.DeleteAsync(id, ct)) { return false; }
        await _audit(actor, "reports.delete", id, existing.Name, ct);
        return true;
    }

    /// <summary>
    /// Validates catalog input and returns the cleaned entry or an error code.
    /// </summary>
    public static (string? Error, ReportCatalogEntry? Entry) Validate(ReportCatalogInput input)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0 || name.Length > 120) { return ("name", null); }
        var description = (input.Description ?? "").Trim();
        if (description.Length > 300) { return ("description", null); }

        var keywords = new List<string>();
        foreach (var raw in input.Keywords ?? Array.Empty<string>())
        {
            var keyword = (raw ?? "").Trim();
            if (keyword.Length == 0) { continue; }
            if (keyword.Length > 60) { return ("keywords", null); }
            if (!keywords.Contains(keyword, StringComparer.CurrentCultureIgnoreCase)) { keywords.Add(keyword); }
        }
        if (keywords.Count > MaxKeywords) { return ("keywords", null); }

        var crmId = (input.CrmReportId ?? "").Trim();
        var url = (input.Url ?? "").Trim();
        if ((crmId.Length == 0) == (url.Length == 0)) { return ("target", null); }
        if (crmId.Length > 0 && !Guid.TryParse(crmId, out _)) { return ("crm_id", null); }
        if (url.Length > 0 && (url.Length > 1000 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))) { return ("url", null); }

        var recordEntity = (input.RecordEntity ?? "").Trim().ToLowerInvariant();
        if (recordEntity.Length > 0 && !EntityName.IsMatch(recordEntity)) { return ("record_entity", null); }
        var recordParam = (input.RecordParam ?? "").Trim();
        if (recordParam.Length > 0 && !ParamName.IsMatch(recordParam)) { return ("record_param", null); }
        if (url.Length > 0 && recordEntity.Length > 0 && recordParam.Length == 0) { return ("record_param", null); }

        return (null, new ReportCatalogEntry
        {
            Name = name,
            Description = description.Length == 0 ? null : description,
            Keywords = keywords,
            CrmReportId = crmId.Length == 0 ? null : Guid.Parse(crmId).ToString("D"),
            Url = url.Length == 0 ? null : url,
            RecordEntity = recordEntity.Length == 0 ? null : recordEntity,
            RecordParam = url.Length > 0 && recordParam.Length > 0 ? recordParam : null,
            Enabled = input.Enabled,
        });
    }
}
