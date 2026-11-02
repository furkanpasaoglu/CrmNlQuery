# Data Access Log and Question Text Viewing

Status: Implemented; question text viewing is off by default.

## Summary
A security or compliance reviewer must be able to answer "who read which CRM tables, when, and how many rows?" without the log becoming a second copy of business data. The service records one metadata row for every CRM data read it performs on behalf of a user. Question text, FetchXML, filter values, record ids and row contents are never recorded. A separate, default-off feature lets permitted administrators read users' question text, with every viewing itself audited.

## Goals
- One append-only log row per executed CRM read, grouped by request id.
- A panel page to browse the log, itself audited.
- Configurable retention, independent of the management audit trail.
- Optional, non-retroactive, fully audited viewing of question text.

## Behavior
### Log rows
`DataAccessRecord`: `Id`, `At`, `User` (systemuserid), `Source`, `Tables`, `Rows`, `Outcome`, `RequestId`.

| Field | Values |
|---|---|
| `Source` | `ask`, `result`, `lookup`, `record_context`, `count`, `summary`, `draft`, `other` |
| `Tables` | root entity plus `link-entity` names, logical names only |
| `Rows` | rows returned to the user |
| `Outcome` | `ok`, `crm_denied`, `error` |
| `RequestId` | groups all rows produced by one question (each chain step, name lookups, context reads); reads not tied to a question get their own id |

The evaluation caller (`Caller.ForEvaluation`) is never logged. A failure to write the log never fails the user's request (warning in the application log).

### Capture point
Capture is done by decorators around the user-impersonating ports, `AccessLoggingQueryExecutor` (over `IQueryExecutor`) and `AccessLoggingRecordSearcher` (over `IRecordSearcher`), plus the record counter. Every code path that reads the CRM as a user goes through them, so no endpoint can forget to log. Table names are extracted from the FetchXML `entity` and `link-entity` name attributes only; no value is read.

### Append-only storage
`IAccessLogStore` offers write, query and purge only: no update or delete. In PostgreSQL (`data_access_log`) triggers reject `UPDATE` and `TRUNCATE` always, and reject `DELETE` unless the retention job sets the session flag `nlquery.purge`. Someone with direct administrative access to the database can remove the triggers; the application cannot.

### Panel and endpoints
- Page "Access log", permission `AccessLog.View`: filters for time range, user and table; at most 500 rows; user names resolved from the CRM, falling back to a shortened id.
- `GET /api/admin/access-log?from=&to=&user=&table=&take=` (`RequireAdmin(AccessLog.View)`).
- Every viewing writes an `accesslog.view` audit entry (who, which filters), rate-limited to one per 10 minutes for the same administrator and filters.

### Question text viewing
- Setting `Access:QuestionTextView` (`off` | `on`, default `off`) and permission `Questions.View`. While `off`, `GET /api/admin/users/questions?account=&take=` returns 404 for everyone, administrators included.
- When `on`, a holder of the permission can read the recent questions of a chosen user. Each request writes a `questions.view` audit entry (who, whose, how many; never the text); this entry is not rate-limited.
- Not retroactive: a history entry is marked viewable (`history.admin_viewable`) only if the setting was on when the question was asked. Entries created while it was off stay invisible even after it is switched on.
- No separate copy: the administrator reads the user's own history row. If the user deletes the chat or the history retention (`History:RetentionDays`) expires it, the question is no longer visible to administrators either.
- Notice: while the setting is on, `GET /api/chats` returns `auditNotice: true` and the widget shows "Your questions may be viewed by administrators for audit purposes."

## Security and privacy
- The log contains metadata only. A question containing a company name must leave no trace of that name in log rows or responses.
- Reading the log and viewing question text are permission-gated and audited.
- The management audit trail (`audit`) is never purged; the access log follows `Retention:SecurityLogDays`.

## Settings and permissions
| Key / permission | Meaning |
|---|---|
| `Retention:SecurityLogDays` | Access-log retention in days (default 365, `0` = keep forever). Does not affect `audit`. |
| `Access:QuestionTextView` | `off` (default) or `on`. |
| `AccessLog.View` | Read the access log. |
| `Questions.View` | Read question text (effective only while the setting is `on`). |

## Out of scope
Replacing the CRM's own audit; alerting rules; showing users their own access log; reads a user performs later inside the CRM from an "Open in CRM" view.
