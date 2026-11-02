using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Domain.Quality.Constants;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Runs the acceptance set automatically each day at the configured hour within a window, once per day.
/// </summary>
public sealed class QualityScheduler
{
    public const int WindowHours = 3;
    public const string Marker = " (scheduled)";

    private readonly QualityRunService _runs;
    private readonly IQualityRunStore _store;
    private readonly Func<QualitySchedule> _schedule;
    private readonly Func<string, CancellationToken, Task<CallerResult>> _resolveCaller;
    private readonly ILogger? _logger;
    private readonly Func<DateTimeOffset> _localNow;
    private DateOnly? _startedDay;
    private DateOnly? _warnedDay;
    private string? _reportedRun;

    public QualityScheduler(QualityRunService runs, IQualityRunStore store, Func<QualitySchedule> schedule, Func<string, CancellationToken, Task<CallerResult>> resolveCaller,
        ILogger? logger = null, Func<DateTimeOffset>? localNow = null)
    {
        _runs = runs;
        _store = store;
        _schedule = schedule;
        _resolveCaller = resolveCaller;
        _logger = logger;
        _localNow = localNow ?? (() => DateTimeOffset.Now);
    }

    /// <summary>
    /// Tells whether a quality run was started by the scheduler.
    /// </summary>
    public static bool IsScheduled(QualityRun run) => run.StartedBy.EndsWith(Marker, StringComparison.Ordinal);

    /// <summary>
    /// Computes the next scheduled run time in server-local time, today if the window is still open.
    /// </summary>
    public DateTimeOffset? NextRun(DateTimeOffset now, QualitySchedule schedule, bool ranToday)
    {
        if (!schedule.Enabled) { return null; }
        var today = At(now, schedule.Hour);
        if (!ranToday && now < today.AddHours(WindowHours)) { return today; }
        return today.AddDays(1);
    }

    /// <summary>
    /// Performs one scheduler check and starts the daily quality run when all conditions are met.
    /// </summary>
    public async Task<ScheduleTick> TickAsync(CancellationToken ct = default)
    {
        var schedule = _schedule();
        if (!schedule.Enabled) { return ScheduleTick.Disabled; }

        var now = _localNow();
        var due = At(now, schedule.Hour);
        if (now < due || now >= due.AddHours(WindowHours)) { return ScheduleTick.NotDue; }

        var today = DateOnly.FromDateTime(now.DateTime);
        if (_startedDay == today) { return ScheduleTick.AlreadyToday; }
        if (await RanTodayAsync(now, ct))
        {
            _startedDay = today;
            return ScheduleTick.AlreadyToday;
        }

        if (string.IsNullOrWhiteSpace(schedule.RunAs))
        {
            WarnOnce(today, "The scheduled quality check is on but nobody is set to run it. Open the Quality check page and save the schedule again.");
            return ScheduleTick.NoOwner;
        }
        CallerResult resolved;
        try
        {
            resolved = await _resolveCaller(schedule.RunAs, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            resolved = new CallerResult(null, "the CRM could not be reached: " + ex.GetType().Name);
        }
        if (resolved.Caller is null)
        {
            WarnOnce(today, $"The scheduled quality check could not start: the account {schedule.RunAs} is not usable ({resolved.Error}). Save the schedule again with an active administrator.");
            return ScheduleTick.CannotResolve;
        }

        try
        {
            var run = await _runs.StartAsync(schedule.RunAs + Marker, resolved.Caller, schedule.Repeat, ct);
            _startedDay = today;
            _logger?.LogInformation("The scheduled quality check started (run {Run}, {Total} case(s), as {Account}).", run.Id, run.Total, schedule.RunAs);
            return ScheduleTick.Started;
        }
        catch (QualityRunException ex) when (ex.Code == "busy")
        {
            _logger?.LogInformation("The scheduled quality check waits: another check is running.");
            return ScheduleTick.Busy;
        }
        catch (QualityRunException ex) when (ex.Code == "empty")
        {
            _startedDay = today;
            _logger?.LogInformation("The scheduled quality check was skipped: the acceptance set has no active cases.");
            return ScheduleTick.Empty;
        }
    }

    /// <summary>
    /// Reports a finished scheduled run once, as a warning when there are failures or regressions.
    /// </summary>
    public async Task ReportFinishedAsync(CancellationToken ct = default)
    {
        var latest = await LatestScheduledAsync(ct);
        if (latest is null || latest.FinishedAt is null || latest.Id == _reportedRun) { return; }
        _reportedRun = latest.Id;
        if (latest.Status != QualityRunStatus.Done)
        {
            _logger?.LogWarning("The scheduled quality check {Run} ended as {Status}: {Error}", latest.Id, latest.Status, latest.Error ?? "no detail");
            return;
        }
        var report = await _runs.ReportAsync(latest.Id, ct);
        if (report is null) { return; }
        var r = report.Value.Report;
        if (r.Regressions.Count > 0 || r.Errors > 0 || r.Failed > 0)
        {
            _logger?.LogWarning("The scheduled quality check {Run} found problems: accuracy {Accuracy}%, {Regressions} regression(s), {Failed} failed, {Errors} error(s). Open the Quality check page for details.",
                latest.Id, r.AccuracyPercent, r.Regressions.Count, r.Failed, r.Errors);
        }
        else
        {
            _logger?.LogInformation("The scheduled quality check {Run} passed: accuracy {Accuracy}% on {Total} case(s).", latest.Id, r.AccuracyPercent, r.Total);
        }
    }

    /// <summary>
    /// Returns the summary of the last scheduled run for the panel card, or null when none exists.
    /// </summary>
    public async Task<ScheduledRunSummary?> LastRunAsync(CancellationToken ct = default)
    {
        var latest = await LatestScheduledAsync(ct);
        if (latest is null) { return null; }
        double? accuracy = null;
        var regressions = 0;
        var errors = 0;
        if (latest.Status == QualityRunStatus.Done && await _runs.ReportAsync(latest.Id, ct) is { } report)
        {
            accuracy = report.Report.AccuracyPercent;
            regressions = report.Report.Regressions.Count;
            errors = report.Report.Errors;
        }
        return new ScheduledRunSummary(latest.Id, latest.Status, latest.StartedAt, latest.FinishedAt, latest.Total, accuracy, regressions, errors, latest.Error);
    }

    /// <summary>
    /// Checks whether a scheduled run already started today.
    /// </summary>
    public async Task<bool> RanTodayAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var runs = await _store.ListRunsAsync(10, ct);
        return runs.Any(r => IsScheduled(r) && DateOnly.FromDateTime(r.StartedAt.ToOffset(now.Offset).DateTime) == DateOnly.FromDateTime(now.DateTime));
    }

    /// <summary>
    /// Returns the current time used by the scheduler.
    /// </summary>
    public DateTimeOffset Now() => _localNow();

    /// <summary>
    /// Checks every minute whether to start a run, and never lets an error stop the service.
    /// </summary>
    public async Task RunLoopAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stopping);
                await ReportFinishedAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "The scheduled quality check loop had a problem ({ExceptionType}); it keeps running.", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stopping); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<QualityRun?> LatestScheduledAsync(CancellationToken ct)
        => (await _store.ListRunsAsync(30, ct)).Where(IsScheduled).OrderByDescending(r => r.StartedAt).FirstOrDefault();

    private static DateTimeOffset At(DateTimeOffset now, int hour) => new DateTimeOffset(now.Year, now.Month, now.Day, Math.Clamp(hour, 0, 23), 0, 0, now.Offset);

    private void WarnOnce(DateOnly day, string message)
    {
        if (_warnedDay == day) { return; }
        _warnedDay = day;
        _logger?.LogWarning("{Message}", message);
    }
}
