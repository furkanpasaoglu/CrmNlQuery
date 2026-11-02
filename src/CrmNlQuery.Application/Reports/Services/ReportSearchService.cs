using CrmNlQuery.Application.Observability.AccessLog;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Reports.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Reports.Services;

/// <summary>
/// Finds CRM reports (SSRS) the user can see with their own permissions and matches questions to them.
/// </summary>
public sealed class ReportSearchService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private const string Fetch = "<fetch count='500'><entity name='report'><attribute name='name'/><attribute name='description'/><attribute name='reportid' alias='_rowid'/>"
        + "<filter><condition attribute='parentreportid' operator='null'/><condition attribute='reporttypecode' operator='in'><value>1</value><value>3</value></condition></filter></entity></fetch>";

    private readonly IQueryExecutor _executor;
    private readonly IMetadataSource _metadata;
    private readonly Func<bool> _enabled;
    private readonly string _crmBaseUrl;
    private readonly IReportCatalogStore? _catalog;
    private readonly IObjectTypeCodeSource? _codes;
    private readonly ILogger? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, (DateTimeOffset At, IReadOnlyList<ReportItem> Reports)> _cache = new Dictionary<Guid, (DateTimeOffset, IReadOnlyList<ReportItem>)>();

    public ReportSearchService(IQueryExecutor executor, IMetadataSource metadata, Func<bool> enabled, string crmBaseUrl, IReportCatalogStore? catalog = null,
        IObjectTypeCodeSource? codes = null, ILogger? logger = null, Func<DateTimeOffset>? clock = null)
    {
        _executor = executor;
        _metadata = metadata;
        _enabled = enabled;
        _crmBaseUrl = crmBaseUrl.TrimEnd('/');
        _catalog = catalog;
        _codes = codes;
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled => _enabled();

    /// <summary>
    /// Tries to find reports matching the question for the caller, using the record on screen when present.
    /// </summary>
    public async Task<ReportOutcome?> TryFindAsync(string question, Caller caller, ResolvedRecord? record = null, CancellationToken ct = default)
    {
        if (!_enabled() || !ReportMatcher.Matches(question)) { return null; }
        var candidates = await CandidatesAsync(caller, ct);
        if (candidates.Count == 0) { return null; }

        var (items, listedAll) = ReportMatcher.Find(question, candidates);
        if (items.Count == 0) { return null; }
        var shown = new List<ReportItem>();
        foreach (var item in items) { shown.Add(await WithRecordAsync(item, record, ct)); }
        return new ReportOutcome(question, shown, listedAll);
    }

    /// <summary>
    /// Runs the same matching as the admin who asks, for the panel's Try box.
    /// </summary>
    public async Task<IReadOnlyList<ReportItem>> TestAsync(string question, Caller caller, CancellationToken ct = default)
    {
        var candidates = await CandidatesAsync(caller, ct);
        return ReportMatcher.Find(question, candidates).Items;
    }

    /// <summary>
    /// Lists CRM reports the admin can see so one can be picked for a catalog entry.
    /// </summary>
    public async Task<IReadOnlyList<ReportItem>> CrmReportsAsync(Caller caller, CancellationToken ct = default)
        => (await ListCrmAsync(caller, ct)).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    private async Task<IReadOnlyList<ReportItem>> CandidatesAsync(Caller caller, CancellationToken ct)
    {
        var crm = await ListCrmAsync(caller, ct);
        var entries = _catalog is null ? new List<ReportCatalogEntry>() : (await _catalog.ListAsync(ct)).Where(e => e.Enabled).ToList();
        if (entries.Count == 0) { return crm; }

        var byId = crm.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var aliased = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ReportItem>();
        foreach (var entry in entries)
        {
            if (entry.CrmReportId is not null)
            {
                if (!byId.TryGetValue(entry.CrmReportId, out var real)) { continue; }
                aliased.Add(real.Id);
                items.Add(real with { Name = entry.Name, Description = entry.Description ?? real.Description, Keywords = entry.Keywords, RecordEntity = entry.RecordEntity });
            }
            else if (entry.Url is not null)
            {
                items.Add(new ReportItem(entry.Id, entry.Name, entry.Description, entry.Url) { Keywords = entry.Keywords, RecordEntity = entry.RecordEntity, RecordParam = entry.RecordParam });
            }
        }
        items.AddRange(crm.Where(r => !aliased.Contains(r.Id)));
        return items;
    }

    private async Task<ReportItem> WithRecordAsync(ReportItem item, ResolvedRecord? record, CancellationToken ct)
    {
        if (record is null || string.IsNullOrEmpty(item.RecordEntity) || !string.Equals(item.RecordEntity, record.Entity, StringComparison.OrdinalIgnoreCase)) { return item; }
        var id = record.Id.ToString("D");
        if (!string.IsNullOrEmpty(item.RecordParam))
        {
            var separator = item.Url.Contains('?') ? "&" : "?";
            return item with { Url = item.Url + separator + Uri.EscapeDataString(item.RecordParam) + "=" + id, ForRecord = record.Name };
        }
        var code = _codes is null ? null : await _codes.GetAsync(record.Entity, ct);
        if (code is null) { return item; }
        return item with { Url = item.Url + "&context=records&recordstype=" + code.Value + "&records=%7b" + id + "%7d", ForRecord = record.Name };
    }

    private async Task<IReadOnlyList<ReportItem>> ListCrmAsync(Caller caller, CancellationToken ct)
    {
        var key = caller.IsEvaluation ? Guid.Empty : caller.UserId;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit) && _clock() - hit.At < CacheFor) { return hit.Reports; }
        }

        IReadOnlyList<ReportItem> items;
        try
        {
            items = await ReadCrmAsync(caller, ct);
        }
        catch (Exception ex) when (ex is CrmException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger?.LogWarning("CRM reports could not be listed ({ExceptionType}); only the catalog is used.", ex.GetType().Name);
            return Array.Empty<ReportItem>();
        }
        lock (_gate)
        {
            _cache[key] = (_clock(), items);
        }
        return items;
    }

    private async Task<IReadOnlyList<ReportItem>> ReadCrmAsync(Caller caller, CancellationToken ct)
    {
        var info = await _metadata.GetEntityAsync("report", ct);
        if (info is null) { return Array.Empty<ReportItem>(); }

        using var accessScope = AccessLogContext.Begin("other");
        ResultTable table;
        try
        {
            table = await _executor.ExecuteAsync(info.EntitySetName, Fetch, caller, ct);
        }
        catch (CrmException ex) when (ex.IsPermissionDenied)
        {
            return Array.Empty<ReportItem>();
        }

        var items = new List<ReportItem>();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var id = table.RowIds is not null && i < table.RowIds.Count ? table.RowIds[i] : null;
            if (!Guid.TryParse(id, out var reportId)) { continue; }
            var row = table.Rows[i];
            var name = row.Count > 0 ? row[0] : "";
            if (string.IsNullOrWhiteSpace(name)) { continue; }
            var description = row.Count > 1 && !string.IsNullOrWhiteSpace(row[1]) ? Shorten(row[1]) : null;
            items.Add(new ReportItem(reportId.ToString("D"), name, description, ViewerUrl(reportId)));
        }
        return items;
    }

    /// <summary>
    /// Builds the CRM report viewer URL for a report id.
    /// </summary>
    public string ViewerUrl(Guid reportId) => $"{_crmBaseUrl}/crmreports/viewer/viewer.aspx?action=run&id=%7b{reportId:D}%7d";

    private static string Shorten(string text)
    {
        var one = string.Join(" ", text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return one.Length <= 160 ? one : one.Substring(0, 157) + "...";
    }
}
