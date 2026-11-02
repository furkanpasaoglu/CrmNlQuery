using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Export.Abstractions;
using CrmNlQuery.Application.Export.Exceptions;
using CrmNlQuery.Application.Export.Models;
using CrmNlQuery.Application.Feedback.Abstractions;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Application.Export.Services;

/// <summary>
/// Streams audit, access-log, usage and feedback records to a CSV or JSON Lines file without buffering them in memory.
/// </summary>
public sealed class ExportService
{
    public const int PageSize = 1000;
    public const int MaxRangeDays = 3660;
    private const int MaxConcurrent = 2;

    private static readonly ConcurrentDictionary<string, byte> Running = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
    private static int _running;

    private readonly IAuditPager? _audit;
    private readonly IAccessLogPager? _accessLog;
    private readonly IQuestionPager? _questions;
    private readonly Func<bool> _questionTextEnabled;
    private readonly IUserNames _names;
    private readonly IMetricsStore _metrics;
    private readonly IFeedbackStore _feedback;
    private readonly AccessService _access;
    private readonly Func<int> _maxRows;
    private readonly Func<DateTimeOffset> _clock;

    public ExportService(IAuditPager? audit, IAccessLogPager? accessLog, IUserNames names, IMetricsStore metrics, IFeedbackStore feedback, AccessService access,
        Func<int> maxRows, Func<DateTimeOffset>? clock = null, IQuestionPager? questions = null, Func<bool>? questionTextEnabled = null)
    {
        _questions = questions;
        _questionTextEnabled = questionTextEnabled ?? (() => false);
        _audit = audit;
        _accessLog = accessLog;
        _names = names;
        _metrics = metrics;
        _feedback = feedback;
        _access = access;
        _maxRows = maxRows;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Checks permission, filters, date range, time zone and concurrency slot, and returns a prepared export plan.
    /// </summary>
    public ExportPlan Prepare(AccessPrincipal actor, ExportRequest request)
    {
        var viewPermission = ExportDatasets.ViewPermission(request.Dataset);
        if (viewPermission is null)
        {
            throw new ExportException("invalid", "Unknown data set.");
        }
        if (!actor.Has(Permissions.ExportRun) || !actor.Has(viewPermission))
        {
            throw new ExportException("forbidden", "You are not allowed to export this data set.");
        }
        if ((request.Dataset == ExportDatasets.Audit && _audit is null) || (request.Dataset == ExportDatasets.AccessLog && _accessLog is null)
            || (request.Dataset == ExportDatasets.Questions && _questions is null))
        {
            throw new ExportException("unavailable", "This data set is not available on this server.");
        }
        if (request.Dataset == ExportDatasets.Questions && !_questionTextEnabled())
        {
            throw new ExportException("unavailable", "Question text viewing is switched off (the Access:QuestionTextView setting), so question text cannot be exported.");
        }
        if (request.To <= request.From || (request.To - request.From).TotalDays > MaxRangeDays)
        {
            throw new ExportException("invalid", "Choose a date range of at most " + MaxRangeDays + " days that ends after it starts.");
        }
        if (!SafeText(request.Kind, 40, allowUpper: false, "._-") || !SafeText(request.Table, 64, allowUpper: false, "_") || !SafeText(request.Account, 200, allowUpper: true, "\\@._- $"))
        {
            throw new ExportException("invalid", "A filter value is not valid.");
        }
        var supportsAccount = request.Dataset == ExportDatasets.Audit;
        var supportsUser = request.Dataset is ExportDatasets.AccessLog or ExportDatasets.Questions;
        var supportsTable = request.Dataset == ExportDatasets.AccessLog;
        var supportsKind = request.Dataset is ExportDatasets.Audit or ExportDatasets.AccessLog;
        if ((!string.IsNullOrWhiteSpace(request.Account) && !supportsAccount) || (request.UserId is not null && !supportsUser)
            || (!string.IsNullOrWhiteSpace(request.Table) && !supportsTable) || (!string.IsNullOrWhiteSpace(request.Kind) && !supportsKind))
        {
            throw new ExportException("invalid", "This data set does not support one of the filters you chose.");
        }
        var cutoff = _clock().AddSeconds(-5);
        if (request.To > cutoff)
        {
            request = request with { To = cutoff > request.From ? cutoff : request.From.AddSeconds(1) };
        }

        var zone = TimeZoneInfo.Local;
        var zoneName = zone.Id;
        string? unavailableZone = null;
        var requestedZone = request.TimeZone?.Trim();
        if (!string.IsNullOrEmpty(requestedZone))
        {
            if (!SafeText(requestedZone, 64, allowUpper: true, "/_+-"))
            {
                throw new ExportException("invalid", "The time zone is not valid.");
            }
            var found = FindZone(requestedZone);
            if (found is null)
            {
                unavailableZone = requestedZone;
            }
            else
            {
                zone = found;
                zoneName = requestedZone;
            }
        }

        var actorKey = actor.Account;
        if (_running >= MaxConcurrent || !Running.TryAdd(actorKey, 0))
        {
            throw new ExportException("busy", "Another export is running. Wait for it to finish and try again.");
        }
        if (Interlocked.Increment(ref _running) > MaxConcurrent)
        {
            Interlocked.Decrement(ref _running);
            Running.TryRemove(actorKey, out _);
            throw new ExportException("busy", "Another export is running. Wait for it to finish and try again.");
        }

        var extension = request.Format == ExportFormat.Csv ? "csv" : "jsonl";
        var contentType = request.Format == ExportFormat.Csv ? "text/csv; charset=utf-8" : "application/x-ndjson; charset=utf-8";
        var fileName = $"nlquery-{request.Dataset}-{request.From.UtcDateTime:yyyyMMdd}-{request.To.UtcDateTime:yyyyMMdd}.{extension}";
        return new ExportPlan(request, fileName, contentType, zone, zoneName, unavailableZone, () =>
        {
            Interlocked.Decrement(ref _running);
            Running.TryRemove(actorKey, out _);
        });
    }

    private static TimeZoneInfo? FindZone(string name)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(name, out var windowsId) && windowsId is not null)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                }
                catch (Exception inner) when (inner is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    return null;
                }
            }
            return null;
        }
    }

    private static bool SafeText(string? value, int max, bool allowUpper, string extra)
    {
        return string.IsNullOrEmpty(value) || (value.Length <= max && value.All(ch => char.IsAsciiLetterOrDigit(ch) && (allowUpper || !char.IsAsciiLetterUpper(ch)) || extra.Contains(ch)));
    }

    /// <summary>
    /// Writes an audit start record, streams the export to the output, then records done, aborted or failed.
    /// </summary>
    public async Task StreamAsync(AccessPrincipal actor, ExportPlan plan, Stream output, CancellationToken ct = default)
    {
        var request = plan.Request;
        var columns = ExportSchema.For(request.Dataset);
        var maxRows = Math.Max(1000, _maxRows());

        await _access.RecordAsync(actor.Account, "export.start", request.Dataset, Describe(plan), ct);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var stream = new HashingStream(output, hasher);
        long rows = 0;
        var truncated = false;
        var outcome = "export.done";
        try
        {
            if (request.Format == ExportFormat.Csv)
            {
                await stream.WriteAsync(ExportFormatter.Bom, ct);
            }
            await WriteTextAsync(stream, ExportFormatter.Header(request.Format, columns), ct);

            await foreach (var values in Rows(plan, ct))
            {
                if (rows >= maxRows)
                {
                    truncated = true;
                    break;
                }
                await WriteTextAsync(stream, ExportFormatter.Row(request.Format, columns, values), ct);
                rows++;
            }
            await stream.FlushAsync(ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is IOException)
        {
            outcome = "export.aborted";
            throw;
        }
        catch (Exception)
        {
            outcome = "export.failed";
            throw;
        }
        finally
        {
            var sha = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            var detail = $"rows={rows}; bytes={stream.Length}; sha256={sha}" + (truncated ? $"; truncated at Export:MaxRows={maxRows}" : "") + (outcome == "export.done" ? "" : "; partial");
            try
            {
                await _access.RecordAsync(actor.Account, outcome, request.Dataset, detail, CancellationToken.None);
            }
            catch (Exception)
            {
            }
        }
    }

    private static string Describe(ExportPlan plan)
    {
        var r = plan.Request;
        var sb = new StringBuilder();
        sb.Append("format=").Append(r.Format == ExportFormat.Csv ? "csv" : "jsonl");
        sb.Append("; from=").Append(r.From.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture));
        sb.Append("; to=").Append(r.To.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(r.Account)) { sb.Append("; account=").Append(r.Account.Trim()); }
        if (r.UserId is { } user) { sb.Append("; user=").Append(user.ToString("D")); }
        if (!string.IsNullOrWhiteSpace(r.Table)) { sb.Append("; table=").Append(r.Table); }
        if (!string.IsNullOrWhiteSpace(r.Kind)) { sb.Append("; kind=").Append(r.Kind); }
        if (r.Dataset == ExportDatasets.Questions) { sb.Append("; question_text=included"); }
        sb.Append("; time_zone=").Append(plan.ZoneName);
        if (plan.UnavailableZone is not null) { sb.Append("; requested_time_zone=").Append(plan.UnavailableZone).Append(" (not available on this server)"); }
        return sb.ToString();
    }

    private static async Task WriteTextAsync(Stream stream, string text, CancellationToken ct)
    {
        if (text.Length > 0)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
        }
    }

    private IAsyncEnumerable<object?[]> Rows(ExportPlan plan, CancellationToken ct) => plan.Request.Dataset switch
    {
        ExportDatasets.Audit => AuditRows(plan, ct),
        ExportDatasets.AccessLog => AccessRows(plan, ct),
        ExportDatasets.Questions => QuestionRows(plan, ct),
        ExportDatasets.Usage => UsageRows(plan.Request, ct),
        _ => FeedbackRows(plan.Request, ct),
    };

    private static string[] Times(DateTimeOffset at, TimeZoneInfo zone)
    {
        var utc = at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var local = TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        return new[] { utc, local, zone.Id };
    }

    private async IAsyncEnumerable<object?[]> AuditRows(ExportPlan plan, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var r = plan.Request;
        AuditCursor? cursor = null;
        while (true)
        {
            var page = await _audit!.PageAuditAsync(r.From, r.To, r.Account, r.Kind, cursor, PageSize, ct);
            foreach (var item in page)
            {
                var t = Times(item.Entry.At, plan.TimeZone);
                yield return new object?[] { t[0], t[1], plan.ZoneName, item.Entry.Actor, item.Entry.Action, item.Entry.Target, item.Entry.Detail };
            }
            if (page.Count < PageSize)
            {
                yield break;
            }
            cursor = new AuditCursor(page[^1].Entry.At, page[^1].Id);
        }
    }

    private async IAsyncEnumerable<object?[]> AccessRows(ExportPlan plan, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var r = plan.Request;
        var query = new DataAccessQuery(r.From, r.To, r.UserId, r.Table) { Source = r.Kind };
        var names = new Dictionary<Guid, string?>();
        AccessCursor? cursor = null;
        while (true)
        {
            var page = await _accessLog!.PageAsync(query, cursor, PageSize, ct);
            await AddNamesAsync(names, page.Select(p => p.User), ct);
            foreach (var rec in page)
            {
                var t = Times(rec.At, plan.TimeZone);
                yield return new object?[] { t[0], t[1], plan.ZoneName, rec.User.ToString("D"), names.GetValueOrDefault(rec.User), rec.Source, string.Join(";", rec.Tables), rec.Rows, rec.Outcome, rec.RequestId.ToString("D") };
            }
            if (page.Count < PageSize)
            {
                yield break;
            }
            cursor = new AccessCursor(page[^1].At, page[^1].Id);
        }
    }

    private async Task AddNamesAsync(Dictionary<Guid, string?> names, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var unseen = ids.Distinct().Where(id => !names.ContainsKey(id)).ToList();
        if (unseen.Count == 0)
        {
            return;
        }
        IReadOnlyDictionary<Guid, string> found = new Dictionary<Guid, string>();
        try
        {
            found = await _names.GetNamesAsync(unseen, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
        foreach (var id in unseen)
        {
            names[id] = found.TryGetValue(id, out var name) ? name : null;
        }
    }

    private async IAsyncEnumerable<object?[]> QuestionRows(ExportPlan plan, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var r = plan.Request;
        var names = new Dictionary<Guid, string?>();
        QuestionCursor? cursor = null;
        while (true)
        {
            var page = await _questions!.PageQuestionsAsync(r.From, r.To, r.UserId, cursor, PageSize, ct);
            await AddNamesAsync(names, page.Select(p => p.Owner), ct);
            foreach (var item in page)
            {
                var t = Times(item.At, plan.TimeZone);
                yield return new object?[] { t[0], t[1], plan.ZoneName, item.Owner.ToString("D"), names.GetValueOrDefault(item.Owner), item.Entity, item.Question };
            }
            if (page.Count < PageSize)
            {
                yield break;
            }
            cursor = new QuestionCursor(page[^1].At, page[^1].Id);
        }
    }

    private async IAsyncEnumerable<object?[]> UsageRows(ExportRequest r, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var summary = await _metrics.GetSummaryAsync(r.From, r.To, ct);
        foreach (var d in summary.Days.OrderBy(x => x.Day))
        {
            yield return new object?[] { d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.Requests, d.Failures, d.AiCalls, d.InputTokens, d.OutputTokens };
        }
    }

    private async IAsyncEnumerable<object?[]> FeedbackRows(ExportRequest r, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var summary = await _feedback.SummaryAsync(r.From, r.To, ct);
        foreach (var d in summary.Days.OrderBy(x => x.Day))
        {
            yield return new object?[] { d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.Up, d.Down };
        }
    }
}
