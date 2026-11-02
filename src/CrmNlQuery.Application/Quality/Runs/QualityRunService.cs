using System.Text.Json;
using CrmNlQuery.Application.Observability.AccessLog;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Quality.Comparison;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Quality.Constants;
using CrmNlQuery.Domain.Quality.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Runs the acceptance set in the background with progress, cancellation, time limit and per-case verdicts.
/// </summary>
public sealed class QualityRunService
{
    public const int MaxRepeat = 3;

    private readonly IQualityStore _cases;
    private readonly IQualityRunStore _runs;
    private readonly IQualityCaseRunner _runner;
    private readonly IQueryExecutor _executor;
    private readonly IMetadataSource _metadata;
    private readonly Func<TimeSpan> _timeout;
    private readonly Func<string, string, string, string, CancellationToken, Task> _audit;
    private readonly ILogger? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new object();
    private CancellationTokenSource? _active;
    private string? _activeId;

    public QualityRunService(IQualityStore cases, IQualityRunStore runs, IQualityCaseRunner runner, IQueryExecutor executor, IMetadataSource metadata, Func<TimeSpan> timeout,
        Func<string, string, string, string, CancellationToken, Task> audit, ILogger? logger = null, Func<DateTimeOffset>? clock = null)
    {
        _cases = cases;
        _runs = runs;
        _runner = runner;
        _executor = executor;
        _metadata = metadata;
        _timeout = timeout;
        _audit = audit;
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Marks runs left queued or running by a previous process as failed after a restart.
    /// </summary>
    public Task<int> RecoverAsync(CancellationToken ct = default)
        => _runs.FailActiveAsync("The server restarted while this check was running. Start it again.", ct);

    /// <summary>
    /// Lists quality runs without loading their results.
    /// </summary>
    public Task<IReadOnlyList<QualityRun>> ListAsync(CancellationToken ct = default) => _runs.ListRunsAsync(30, ct);

    /// <summary>
    /// Returns the quality run with the given id from the run store, or null when it does not exist.
    /// </summary>
    public Task<QualityRun?> GetAsync(string id, CancellationToken ct = default) => _runs.GetRunAsync(id, ct);

    /// <summary>
    /// Builds a run's report from its results and the previous completed run.
    /// </summary>
    public async Task<(QualityRun Run, IReadOnlyList<QualityResult> Results, QualityReport Report)?> ReportAsync(string id, CancellationToken ct = default)
    {
        var run = await _runs.GetRunAsync(id, ct);
        if (run is null) { return null; }
        var results = await _runs.ListResultsAsync(id, ct);
        var before = await _runs.PreviousDoneRunAsync(run, ct);
        var previous = before is null ? null : await _runs.ListResultsAsync(before.Id, ct);
        return (run, results, QualityReportBuilder.Build(results, previous));
    }

    /// <summary>
    /// Creates a quality run and starts it in the background, returning immediately.
    /// </summary>
    public async Task<QualityRun> StartAsync(string actor, Caller caller, int repeat, CancellationToken ct = default)
    {
        if (repeat < 1 || repeat > MaxRepeat) { throw new QualityRunException("invalid", $"Repeat must be between 1 and {MaxRepeat}."); }
        var cases = await _cases.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusActive), ct);
        if (cases.Count == 0) { throw new QualityRunException("empty", "The acceptance set has no active cases."); }

        var run = new QualityRun { StartedBy = actor, StartedAt = _clock(), Status = QualityRunStatus.Queued, Repeat = repeat, Total = cases.Count };
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (_active is not null) { throw new QualityRunException("busy", "A quality check is already running. Wait for it to finish or cancel it."); }
            _active = cts;
            _activeId = run.Id;
        }
        try
        {
            await _runs.AddRunAsync(run, ct);
            await _audit(actor, "quality.run", run.Id, $"{cases.Count} case(s), repeat {repeat}", ct);
        }
        catch
        {
            Release(cts);
            throw;
        }
        _ = Task.Run(() => RunAsync(run, cases, caller, cts));
        return run;
    }

    private void Release(CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_active, cts)) { _active = null; _activeId = null; }
        }
        cts.Dispose();
    }

    /// <summary>
    /// Requests cancellation of a running quality run.
    /// </summary>
    public bool Cancel(string id)
    {
        CancellationTokenSource? cts;
        lock (_gate) { cts = _activeId == id ? _active : null; }
        if (cts is null) { return false; }
        try { cts.Cancel(); } catch (ObjectDisposedException) { return false; }
        return true;
    }

    private async Task SaveRun(QualityRun run)
    {
        try { await _runs.UpdateRunAsync(run, CancellationToken.None); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Could not save quality run {Run}", run.Id); }
    }

    private async Task RunAsync(QualityRun run, IReadOnlyList<QualityCase> cases, Caller caller, CancellationTokenSource cts)
    {
        using var timeout = new CancellationTokenSource(_timeout());
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeout.Token);
        try
        {
            run = run with { Status = QualityRunStatus.Running };
            await SaveRun(run);
            var before = await _runs.PreviousDoneRunAsync(run, CancellationToken.None);
            var previousFingerprints = before is null
                ? new Dictionary<string, string?>()
                : (await _runs.ListResultsAsync(before.Id, CancellationToken.None)).ToDictionary(r => r.CaseId, r => r.Fingerprint);

            foreach (var item in cases)
            {
                linked.Token.ThrowIfCancellationRequested();
                var result = await RunCaseAsync(run, item, caller, previousFingerprints.GetValueOrDefault(item.Id), linked.Token);
                await _runs.AddResultAsync(result, CancellationToken.None);
                run = run with { Done = run.Done + 1 };
                await SaveRun(run);
            }
            var all = await _runs.ListResultsAsync(run.Id, CancellationToken.None);
            var previous = before is null ? null : await _runs.ListResultsAsync(before.Id, CancellationToken.None);
            var report = QualityReportBuilder.Build(all, previous);
            run = run with { Status = QualityRunStatus.Done, SummaryJson = JsonSerializer.Serialize(new { report.Total, report.Passed, report.Failed, report.Flaky, report.Errors, report.Skipped, report.Unreadable, report.AccuracyPercent }) };
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            run = run with { Status = QualityRunStatus.Cancelled };
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            run = run with { Status = QualityRunStatus.Failed, Error = "The check took too long and was stopped." };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Quality run {Run} failed", run.Id);
            run = run with { Status = QualityRunStatus.Failed, Error = "The check could not be completed. See the server log." };
        }
        finally
        {
            run = run with { FinishedAt = _clock() };
            await SaveRun(run);
            Release(cts);
        }
    }

    private async Task<QualityResult> RunCaseAsync(QualityRun run, QualityCase item, Caller caller, string? previousFingerprint, CancellationToken ct)
    {
        QualityResult Make(string verdict, string detail, int passes = 0, int runs = 0, int ms = 0, int? rows = null, string? fingerprint = null, bool changed = false)
            => new QualityResult
            {
                RunId = run.Id, CaseId = item.Id, Question = item.Question, Entity = item.Entity, Verified = item.Verified, Verdict = verdict, Passes = passes, Runs = runs,
                Detail = detail, DurationMs = ms, ReferenceRows = rows, Fingerprint = fingerprint, DataChanged = changed,
            };

        var info = await _metadata.GetEntityAsync(item.Entity, ct);
        if (info is null || !await StoredQueryCheck.IsUsableAsync(item.ReferenceFetch, _metadata, ct))
        {
            return Make(QualityVerdict.Skipped, "The case uses data that is closed or no longer available.");
        }

        using var accessScope = AccessLogContext.Begin("other");
        ResultTable reference;
        try
        {
            reference = await _executor.ExecuteAsync(info.EntitySetName, ReferenceQuery.WithRowCap(item.ReferenceFetch), caller, ct);
        }
        catch (CrmException ex) when (ex.IsPermissionDenied)
        {
            return Make(QualityVerdict.Unreadable, "Your CRM permissions do not allow reading this data.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Quality case {Case}: the reference query failed", item.Id);
            return Make(QualityVerdict.Error, "The reference query could not run.");
        }

        var fingerprint = ReferenceQuery.Fingerprint(reference);
        var changed = previousFingerprint is not null && previousFingerprint != fingerprint;
        var rule = ParseRule(item.CompareJson);
        var hasOrder = ReferenceQuery.HasOrder(item.ReferenceFetch);

        var passes = 0;
        var errors = 0;
        var unreadable = 0;
        var elapsed = 0;
        var lastDetail = "";
        for (var i = 0; i < run.Repeat; i++)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = await _runner.RunAsync(item, caller, ct);
            elapsed += candidate.DurationMs;
            if (candidate.Kind == "unreadable") { unreadable++; lastDetail = candidate.Message; continue; }
            if (candidate.Kind != "answer" || candidate.Table is null) { errors++; lastDetail = candidate.Message; continue; }
            var compared = ReferenceComparer.Compare(candidate.Table, reference, rule, hasOrder, showValues: false);
            if (compared.Pass) { passes++; }
            lastDetail = compared.Detail;
        }

        var average = run.Repeat == 0 ? 0 : elapsed / run.Repeat;
        if (unreadable == run.Repeat) { return Make(QualityVerdict.Unreadable, lastDetail, 0, run.Repeat, average, reference.Rows.Count, fingerprint, changed); }
        var verdict = passes == run.Repeat ? QualityVerdict.Pass : passes > 0 ? QualityVerdict.Flaky : errors == run.Repeat ? QualityVerdict.Error : QualityVerdict.Fail;
        var detail = verdict == QualityVerdict.Pass ? "ok" : lastDetail;
        return Make(verdict, detail, passes, run.Repeat, average, reference.Rows.Count, fingerprint, changed);
    }

    private static CompareRule ParseRule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new CompareRule(); }
        try { return JsonSerializer.Deserialize<CompareRule>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new CompareRule(); }
        catch (JsonException) { return new CompareRule(); }
    }
}
