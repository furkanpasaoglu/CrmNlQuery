# Guardrails, Limits and Runtime Settings

Status: Implemented.

## Summary
The tool runs against a production CRM with real users, so its failure modes are bounded. This spec covers input and content limits, per-user and global capacity limits, an end-to-end deadline, a cloud token budget, an emergency stop, sanitizing of CRM-sourced text in prompts, fixed user-facing errors, security headers, and a runtime settings mechanism that lets an administrator change limits without editing files or restarting. Defaults are restrictive; settings loosen them.

## Goals
- A single user, a prompt-injected record name or a huge request cannot exhaust the model server, the CRM or the process.
- Internal details (host names, response bodies) never reach end users.
- Operators can pause questions or adjust limits immediately from the panel.

## Behavior
### Input and content limits
| Limit | Setting | Default |
|---|---|---|
| Question length (rejected before any model call) | `Limits:MaxQuestionChars` | 1000 |
| Link-entities per generated query | `Limits:MaxLinkEntities` | 4 |
| Link nesting depth | `Limits:MaxLinkDepth` | 3 |
| Conditions per query | `Limits:MaxConditions` | 25 |
| Record name length sent to the model | `Limits:MaxRecordNameChars` | 120 |

Request bodies are capped at 64 KB for the API, 256 KB for document management and 1 MB otherwise (HTTP 413). `PlanValidator` enforces the query-cost limits and returns the violation to the model as correction feedback.

### Capacity and time
| Limit | Setting | Default |
|---|---|---|
| Questions per user per minute | `Limits:QuestionsPerMinute` | 20 |
| Questions per user per day (UTC) | `Limits:QuestionsPerDay` | 500 |
| Concurrent questions per user | `Limits:MaxConcurrentPerUser` | 2 |
| Concurrent questions overall | `Limits:MaxConcurrentTotal` | 8 |
| End-to-end deadline, seconds | `Limits:AskDeadlineSeconds` | 120 |
| Daily cloud-model token budget (`0` = unlimited) | `Limits:CloudDailyTokenBudget` | 0 |
| Emergency stop for asking questions | `Limits:AskEnabled` | true |

`AskGate` decides (pure, with an injected clock): kill switch, per-user minute and day quotas, per-user and global concurrency. Rate and quota rejections return 429 with `Retry-After`; a full queue or a disabled switch returns 503 with an explanatory message and `Retry-After`. Administrators are not exempt from question limits; management endpoints have their own small limit. `AskGuard` wraps a question with the gate and the end-to-end deadline; exceeding the deadline cancels the work and answers 504, and is recorded as a timeout. If the gate itself fails unexpectedly the question is not rejected (critical log). A user-pressed stop is recorded as `cancelled`, and quota is not refunded.

When the cloud budget is exhausted (`BudgetedLlmClient`, `CloudBudget`) no further cloud call is made and a clear error is returned. It exists to bound cost when a cloud provider is explicitly enabled and is not used with a local model. Rejected requests are recorded as blocked requests with fixed reason kinds (never the question text) and shown on the dashboard.

### Prompt-injection hygiene
All CRM-sourced text placed in a prompt (record names, labels) passes through `PromptText`: control characters and line breaks are removed, length is capped, and the text is quoted as data under a heading stating that it is not instructions. Every plan still passes `PlanValidator` and runs read-only.

### Fixed user-facing errors
Users receive fixed, understandable messages (`UserMessages`, selected by outcome `kind`). Details such as server addresses and response bodies go only to the log. Messages that concern the user, such as permission errors, are preserved.

### Security headers
`X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer` on all responses; `Content-Security-Policy` with `default-src 'self'` and `frame-ancestors 'none'` on HTML pages, without `unsafe-inline` (page scripts and styles are external files; the admin page uses per-request nonces for styles). API responses use `Cache-Control: no-store`; static files are revalidated with `Cache-Control: no-cache`. HSTS is left to the TLS-terminating reverse proxy.

## Runtime settings
Changing a value in a file and restarting is too slow during an incident, so an allow-listed set of settings is managed from the panel:
- Managed settings: the `Limits:*` keys above, knowledge base switches (`Knowledge:Enabled`, `MinSimilarity`, `TopK`, `MaxChars`), `Planning:CacheMinutes`, retention days and others listed in `SettingsCatalog`. Each has a type, minimum and maximum, and a default; out-of-range values are rejected on the server.
- Never managed from the panel (file or environment only): secrets and API keys, connection strings, identity mode and the bootstrap administrator, CORS, the cloud permission and the model provider choice, because a wrong or malicious value would open a hole or lock administrators out.
- Storage: PostgreSQL `runtime_settings` (in-memory with a warning when there is no database). Precedence: panel/database value, then file/environment, then default; "reset to default" deletes the stored value. Permission `Settings.Manage`; some keys require an additional permission (`Access:DataPolicy` needs `DataPolicy.Manage`). Every change is audited.
- Consumers read values at each use (`Func`/interface), not once at startup. A background loop refreshes the settings cache every 30 seconds, and a change applies immediately on the server that wrote it; in a multi-server deployment others converge within about 30 seconds. If the store cannot be read, the restrictive file/default values apply.
- Panel page "Settings", grouped (Capacity, Content, Knowledge, Planning, Retention, Features), showing each value's source, with a two-step pause control for the emergency stop. The management panel is unaffected by the pause.

## Limitations
Quota, concurrency and cloud-budget counters are in memory and per process; a multi-server deployment needs a shared counter. Reverse proxy, WAF and TLS settings belong to the deployment environment.

## Out of scope
Per-column hiding (covered by CRM field security and the data access policy), shared counters across servers, cloud rerankers.
