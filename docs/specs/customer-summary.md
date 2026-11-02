# Customer Summary

Status: Implemented.

## Summary
"Summarize Acme" in the chat, or "summarize this company" while a CRM account or contact form is open, returns a one-screen summary: basic facts, open opportunities, recent activities, open cases, contracts and orders, and other related tables. All sections are produced by fixed queries and fixed templates in code, under the caller's identity. The model neither writes the summary nor sees any row.

## Goals
A fast answer to "where do we stand with this customer?" before a meeting or call. Out of scope by design: note or email body text (only counts and record names are shown) and model-written prose.

## Behavior
- **Intent** is recognized by rule (`SummaryIntent`: summarize/summary/overview/briefing/"brief me" and Turkish equivalents such as "özetle", diacritic-folded) **and** a single resolved account or contact (by name or by the open form record). A follow-up question or a chain step is never a summary. Analyzer and planner prompts are unchanged; no model call happens.
- Sections (`Application/Summary`, `SummaryBuilder`), in fixed order:
  1. **Basic**: owner, creation date, and the first columns of the entity's default view (system and tracking fields skipped).
  2. **Open opportunities** (state open): count, total (if an amount field exists), largest 3.
  3. **Activities**: last 5, and "last contact" (the newest completed activity). No content text.
  4. **Open cases** (state active): count, oldest, priority distribution.
  5. **Contracts** (active, ending within 90 days) and **orders** (open).
  6. **General sections**: every other open table directly linked to the record through a lookup (custom tables included): count plus the newest 3 record names. A table linked through one intermediate general table is shown as "{Table} (via {Intermediate})" (at most 8 such sections; only the first lookup of the intermediate table is used).
- The known sections depend on table names. A section is omitted when its table does not exist, is closed by the data access list, a field it needs (for example a state code) is closed, or the CRM denies the read. Empty sections say so; sections with no table do not appear.
- Per-section isolation: each section has its own timeout (`Summary:SectionTimeoutSeconds`) and error handling; a failure shows only that section as unavailable with a fixed message (detail only in the log). At most 4 queries run in parallel. Cancelling stops all sections. A CRM aggregate-limit error shows the count as "50,000+".
- The result kind is `SummaryOutcome` (kind `summary`); the UI composes the sentences. Record rows link to the CRM record (`recordUrlBase`). Each section can offer "Open in CRM" (see `open-in-crm.md`) when that feature is on and permitted.
- **History:** a `kind='summary'` entry with a root-record FetchXML is stored; reopening a chat recomputes the summary (no stored figures). Summaries are ignored for follow-up context, filter editing, Open in CRM of the whole result, counting and voting.

## Security and privacy
- All queries run through the query executor as the caller (impersonation), so record-level permissions apply and counts include only visible records. The schema comes from the policy-filtered metadata source: closed tables and fields never enter a query.
- Every query is recorded in the data access log with source `summary`, grouped under one request id.
- A summary counts as one question for the quota (`AskGuard`/`AskGate`); the time limit applies; metrics record outcome "answer" with zero model calls.
- Related tables are discovered from the root entity's relationships in the filtered metadata, not from a fixed list.

## Design
- The builder works on a `Target` record per section so discovery, list query and count query come from one source. `ListFetchXmlAsync`/`ListFetchesAsync` give a section's list query for Open in CRM, and `SummarySection.CanOpen` is computed in one discovery pass.
- Widget: collapsible section cards with a count line, up to 5 rows and "Open" links, plus "Refresh" on top.

## Settings
`Features:Summary` (default on), `Summary:SectionTimeoutSeconds` (default 15).

## Out of scope
Notes and email text, the parent account on a contact summary, a summary view on the result page, voting on summaries, combining with chained questions, configurable section lists, free-text model summaries, tables reachable through more than two hops.
