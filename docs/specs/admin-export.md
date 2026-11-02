# Administrator Export

Status: Implemented.

## Summary
Security and compliance teams need records as files, beyond browsing them in the panel: evidence for "who accessed which customer data in the last three months", post-incident review, and import into a SIEM. The export streams log data to the browser as a download.

## Goals
- Data sets: audit log (administrator actions, permission changes, denied attempts), data access log (who, when, which tables, how many rows), usage statistics, feedback statistics. Question text is not part of any default data set.
- Formats: CSV (Excel) and JSON Lines (SIEM).
- Date range plus filters (user, table, action kind). Export has its own permission.
- Every export is audited, including a SHA-256 of the file.
- Large ranges are streamed. The file only goes to the browser; there is no e-mail or automatic delivery.

## Design
1. **Permissions:** `Export.Run` ("Export logs to a file") plus the view permission of each data set: audit -> `Audit.View`, data access -> `AccessLog.View`, usage and feedback -> `Dashboard.View`, question texts -> `Questions.View` and `Access:QuestionTextView=on`. Export never grants more than the screen already shows. Administrator has all of them.
2. **Usage and feedback are daily totals**, not per-request or per-person rows (day, request count, failures, median/p95 duration, tokens, feedback up/down and reason counts). Per-user rows already exist as the access log.
3. **Two audit rows per export:** `export.start` when the stream begins (who, data set, format, range, filters; `requested_time_zone` is noted when the requested zone is unavailable) and `export.done` at the end (rows, bytes, SHA-256, duration, `truncated` flag). If writing the start row fails, no byte is sent. The hash cannot be inside the file, so it lives in `export.done`; the downloaded file is compared with a file-hash tool. A client that aborts produces `export.aborted`; a failure produces `export.failed`. The audit log is never purged. The hash covers the bytes the server sent.
4. **Streaming:** keyset pagination (`At` + `Id`) over the stores (`IAuditPager`, `IAccessLogPager`; in-memory and PostgreSQL), groups of 1,000, `IAsyncEnumerable`, written straight to the response body with buffering disabled. A safety cap `Export:MaxRows` ends the file at that row and records "truncated". Concurrency: 1 export per administrator and 2 per server (otherwise 429). Unsupported filters return 400 rather than being ignored, and the range end is pinned to a few seconds before the start so late-written rows are not half included.
5. **Data access log user column:** the system user id plus the resolved account name (empty if unresolvable). Tables are joined with `;`. The log never contains question text, FetchXML, filter values or record ids.
6. **Time:** `time_utc` (ISO-8601, `Z`), `time_local` (ISO-8601 with offset), `time_zone`. The local zone is the IANA name sent by the browser (`tz`); if absent or unavailable the server zone is used and the file states which zone applies.
7. **CSV:** UTF-8 with BOM, comma separator, RFC 4180 quoting, CRLF. Injection escaping applies to text columns only (numbers stay numbers): cells starting with `=`, `+`, `-`, `@`, tab or CR get a leading `'`. JSON Lines: one object per line, same field names, numbers as numbers.
8. **Question text data set** (`questions`) is separate: selectable only when the setting is on and the user has the permission, not retroactive (only questions asked while the setting was on), marked in `export.start`, and never selected by default.
9. **Panel:** an "Export" page (Administration): data set, format, range (default last 30 days), filters, a pre-download validation (`?check=true`), a browser download (`GET /api/admin/export/{dataset}`, `Content-Disposition: attachment`, `no-store`), a column dictionary, and a "how to verify a file" note.
10. **Column dictionary** has a single source in server code: `GET /api/admin/export/schema`. The page and the documentation use the same list.

## Endpoints
`GET /api/admin/export/{dataset}?format=csv|jsonl&from&to&account&user&table&kind&tz` and `GET /api/admin/export/schema`, both behind `RequireAdmin(Export.Run)`.

## Settings
| Key | Default | Meaning |
|---|---|---|
| `Export:MaxRows` | 1,000,000 (range 1,000 to 10,000,000) | Row cap per export |

## Out of scope
E-mail or scheduled export, direct SIEM streaming, file signing (the hash is in the audit record), application logs, per-user usage rows, server-side file storage (the file is only a download stream).

## Risks
Reverse proxies may buffer a large stream; proxy response buffering must be considered in deployment. Resolving many distinct user names is done in one cached batch.
