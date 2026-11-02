# Scheduled Quality Runs

Status: Implemented; off by default.

## Summary
Runs the acceptance set automatically on a nightly schedule so regressions (a previously passing case now failing) are caught without an administrator watching. Runs, reports, regression calculation and the Quality page already exist (`quality-checks.md`); this feature only adds unattended starting and status display.

## Behavior
- `QualityScheduler` checks once a minute. A run starts when the schedule is enabled, the local time is at or after `Quality:ScheduleHour` and inside the window `[hour, hour + 3 h]`, and no scheduled run exists for today. It calls the existing `QualityRunService.StartAsync`.
- At most one scheduled run per day, including across restarts (the date of the last scheduled run is read from the run records). If the window is missed (for example the server was down), that day is skipped, so heavy runs do not start during working hours.
- If another run is in progress, the scheduler waits and retries each minute inside the window.
- No active cases: no run is started.
- **Identity.** The run uses the CRM rights of the administrator who saved the schedule, resolved with `CallerIdentityService.GetCallerAsync`. The account is written by the server from the saving administrator's Windows account; clients cannot set it. If the account cannot be resolved or is disabled, or the CRM is unreachable, the run does not start, a warning is logged at most once a day, and the panel shows that it could not run.
- `StartedBy` is `"<account> (scheduled)"`. Audit records match manual runs (`quality.run`); schedule changes are audited as `quality.schedule` (enabled/hour/repeat only).
- **Notification.** When a scheduled run finishes, one log entry is written: Warning if there are regressions, failures or errors, Information if clean. There is no e-mail or chat notification. The log page keeps only entries at or above `Logging:Database:MinimumLevel`, so only problem runs appear there by default; a clean run is visible in the file log and on the card.

## Panel and endpoints
The Quality check page has a "Scheduled check" card: enable/disable, hour, repeat, "runs with the saving administrator's rights", next run time, and a summary of the last scheduled run (status, accuracy, regression count). Endpoints: `GET/PUT /api/admin/quality/schedule` (`Quality.Manage`).

## Settings
| Key | Default | Meaning |
|---|---|---|
| `Quality:ScheduleEnabled` | off | Master switch |
| `Quality:ScheduleHour` | 3 | 0-23, server local time |
| `Quality:ScheduleRepeat` | 1 | 1-3 repeats per case |
| `Quality:ScheduleRunAs` | empty | Account; written only by the schedule endpoint, not by the generic settings endpoint |

All four require `Quality.Manage` (`Settings.Manage` alone is not enough), so nobody can pick another account to run as. Changes apply without restart.

## Security and privacy
No new data flow: the run follows the manual path (administrator's rights, data policy, access log source "other", no rows stored). A local model uses the GPU overnight; if a cloud provider is configured, the daily token budget applies.

## Out of scope
E-mail or chat notifications, multiple schedules, weekly or monthly schedules, running a subset of cases.
