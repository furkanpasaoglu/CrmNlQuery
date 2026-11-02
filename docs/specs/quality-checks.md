# Quality Checks

Status: Implemented.

## Summary
An integrator can build an acceptance set of questions with known-correct answers, re-run it on demand from the admin panel, and see what regressed after an update or a schema change. No command line is needed. Three sources feed the set: generation from the schema (see `starter-acceptance-set.md`), "Add to acceptance set" on a real answer (reviewer-approved), and manual retire/delete.

## Goals
- Persistent acceptance set (PostgreSQL, with an in-memory store for development).
- Admin page "Quality check", and a widget action for reviewers.
- Background runs through the product's own pipeline and model.
- Report: accuracy, regressions, latency, data drift.
- Export in the eval case format.

Out of scope: saving multi-step or chained answers (resolved identifiers go stale), LLM judges (result-set comparison needs no model), automatic cases from live questions (human approval is mandatory), storing result rows.

## Design rules
- **Human approval is required.** A thumbs-up never becomes a reference automatically. Saving requires the `Quality.Manage` permission.
- **The stored reference is the executed FetchXML, not rows.** Rows are never stored.
- **Weak signal is refused.** Answers with zero rows or truncated answers (500+) cannot be saved; the user is told why.
- **Comparison rule per case** (`compare`: order, columns, tolerance, ties), same semantics as `reference-query-evaluation.md`.
- **Data drift is reported separately** (DATA-CHANGED / DATA-MISSING) and is not counted as a model failure.
- **No leakage.** Stored cases never enter model prompts, the knowledge base or the plan cache. During runs the plan and analysis caches are disabled.
- **Repeat:** a run repeats each case 1-3 times; a case that passes some but not all repeats is "flaky".

## Data model
- `quality_cases`: id, created_by, created_at, source (`marked`, `generated`, `suggested`), status (`active`, `retired`, `pending`), verified, language, question, entity, answers (column answers), reference_fetch, compare, template, note, fingerprint, last_status.
- `quality_runs`: id, started_by, started_at, finished_at, status (`queued`, `running`, `done`, `cancelled`, `failed`), repeat, total, done, summary, error.
- `quality_results`: run_id, case_id, passes, runs, verdict, detail, duration_ms, reference_rows, fingerprint, data_changed.

Cases are kept until deleted or retired. Run results are purged after `Retention:QualityRunDays`. Export: `GET /api/admin/quality/cases/export` returns the eval case JSON (usable with the eval runner's `--cases`); it contains no customer rows.

## Behavior
**Case management.** List, add, approve, retire, restore and delete cases; generate from the schema per entity (`GET /api/admin/quality/entities`, `POST /api/admin/quality/generate`). The panel shows a model-free query explanation (`QueryExplainer`) instead of raw FetchXML; raw FetchXML appears only when `Features:ShowFetchXml` is on.

**Add to acceptance set.** `POST /api/result/{id}/quality/save`. The caller must own the result and hold `Quality.Manage` (checked on every call). Rejected: other users' results, summaries, drafts, chained answers, edited or suggested-follow-up questions, closed data, empty or truncated results, duplicates, and a full set. The result is re-run under the caller's rights to confirm it is readable. The reviewer must tick a confirmation ("this answer is correct") and may choose the comparison rule (ordered/unordered, reference/exact columns). The case is created verified; column-question answers are taken from the executed FetchXML.

**Run.** A background job: one active run per server, cancellable, bounded by `Quality:RunTimeoutMinutes`, progress stored in the database; a run left running by a restart is marked failed. For each case:
- a case using data now closed by the data policy is skipped (`StoredQueryCheck`), not executed;
- the reference query runs under the administrator's own CRM rights (unreadable tables are reported "unreadable", not as model failures);
- the candidate runs through the live pipeline (a separate pipeline copy with no plan/analysis cache, unaware of the reference; model calls are metered into a throwaway store so dashboard figures are unaffected; access logging and the data policy apply as usual);
- verdict: pass / fail / flaky / error.

**Report.** Accuracy counts only verified, counted cases (skipped and unreadable cases are excluded). Regressions are cases that passed in the previous finished run and now fail, flake or error. The report also lists fixed cases, problem cases with an identifier-free expected-versus-actual summary (no cell values), latency (average, p95, max), and data drift flagged per case.

**Endpoints.** `POST/GET /api/admin/quality/runs`, `GET /api/admin/quality/runs/{id}`, `POST /api/admin/quality/runs/{id}/cancel` (`Quality.Manage`).

## Security and privacy
- A saved question text comes only from the reviewer's own question; other users' questions are never visible.
- Stored: question text, table, executed FetchXML, rule, column answers, language, creator account, dates, status. Not stored: result rows.
- Resolved record identifiers are not shown in the panel (the explanation is shown instead).
- Runs use the administrator's own CRM rights (impersonation); the report carries no rows.
- Audit actions: `quality.mark`, `quality.approve`, `quality.retire`, `quality.delete`, `quality.generate`, `quality.run`, `quality.schedule`; the target is a case or run identifier, never question text.

## Settings
`Features:Quality` (default on), `Quality:MaxCases` (500), `Quality:RunTimeoutMinutes` (240), `Retention:QualityRunDays` (180). Permission: `Quality.Manage`.

## Notes
A saved query bound to a resolved record becomes DATA-MISSING if that record is deleted. A reviewer can save a wrong answer as correct; mitigations are the on-screen explanation and row count, the mandatory confirmation, visible source and creator, and the ability to retire or delete. Generated cases start unverified.
