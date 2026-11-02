# Features

Every feature has a design spec in [`docs/specs`](specs/README.md). Features marked *off by default* are switched on in the admin panel under *Settings*.

## Asking questions

- Natural language to a validated, read-only FetchXML query, executed as the signed-in user, shown as a table (Turkish and English questions)
- Record name resolution with clickable "which one do you mean?" choices
- Column choice: default view, name only, or columns named in the question
- Plan validator: allow-listed constructs only, cost limits, type and alias rules; errors go back to the model, never to the CRM
- Plan cache and analysis cache
- Conditional plans ("if there are none, show ...") and dependent multi-step plans ("the contacts of the account with the most open opportunities"); the code carries identifiers between steps, the model never sees row data
- Follow-up questions ("only the ones in Istanbul") and form context ("this account's contacts")
- Duplicate-row prevention for one-to-many filters, with an "Open" link on every row
- Total record count and narrowing suggestions for large results
- Open in CRM: show a large result in the CRM's own grid (off by default)
- Query explanation in plain language, with filter chips that can be removed or changed without calling the model
- Next-step suggestions (narrow, group, related records, time range, sort)
- Charts for aggregate results (bar, horizontal bar, line) with PNG download
- Customer summary built by code from fixed queries ("summarize this account")
- Report finder: find and open CRM (SSRS) reports, plus an administrator-managed report catalog
- Business glossary: company terms mapped to filters, enforced in code
- E-mail drafts from a template the model writes and the code fills (nothing is ever sent)
- Record creation from chat with preview and explicit confirmation (off by default)
- Customization design assistant: a written design document for new tables and fields; it never changes the CRM

## Chat window

- Chat inside CRM pages, with conversation list, search, pinning, renaming, deletion and retention
- Stop button with real cancellation on the server
- Slash commands (`/help`, `/new`, `/edit`, `/history`, `/clear`)
- Thumbs up / down feedback with opt-in sharing of the question for review
- Dark, light and automatic themes, resizing, settings page, Turkish and English
- Result page with search, paging, print and re-run

## Administration

- Users, roles, permissions and CRM role/team to role mapping, with an audit trail
- Data access log (who read which tables and how many rows) and an optional, announced question-text view
- Data access policy: administrators choose which tables and fields can be asked about, on top of CRM security
- Knowledge base (hybrid vector and full-text search) and business glossary
- Quality checks: acceptance set, scheduled runs, regression report
- Feedback review, usage dashboard, logs, export (CSV / JSON Lines), runtime settings with an emergency stop
- Guided tours and a "How it works" card on every page

## Security

- CRM-proof sign-in: users are identified by the CRM, never by a separate password
- Every CRM read runs as the caller; an empty identity never falls back to the service account
- Least-privileged service account with an allow-listed read policy for its own calls
- Quotas, concurrency limits, end-to-end timeout, daily cloud token budget
- Content-Security-Policy without inline scripts, cross-site request guard, size limits

## Models

- Ollama (default), vLLM (OpenAI-compatible) and the Claude API (for non-sensitive data only)
- Per-step thinking settings, context-window and prompt-size metering

## Evaluation

- Evaluation runner with packs for queries, chains, context, knowledge, glossary, charts, drafts, summaries, reports and record creation
- Reference-query mode: the expected result is computed from the live data, not hard-coded
- Starter acceptance set generated from the schema
- Load and latency measurement scripts

## Operations

- Docker Compose deployment, first-run setup wizard, encrypted settings
- PostgreSQL schema migrations, startup wait page, metrics and logs

## Planned

- AD FS-only CRM endpoints for the service account
- Packaging the chat window as a CRM web resource solution
- Shared counters for multi-instance deployments
- Saving an e-mail draft as a CRM activity
