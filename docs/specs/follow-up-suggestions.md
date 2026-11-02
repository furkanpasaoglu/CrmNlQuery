# Follow-up Suggestions

Status: Implemented.

## Summary
Under a list answer the widget shows one "Next:" row with up to four one-click follow-ups. They are produced by code from the query, the schema and the result, with no model call, so they are instant and do not load the model.

## Goals
Support the typical analyst flow: narrow, then group or aggregate, sometimes move to related records.

## Behavior
Suggestion kinds, in priority order (at most 4 in total):

| Kind | Example | Rule |
|---|---|---|
| narrow (max 2) | "Only Open" | Most frequent option values in the result, only when at least two distinct values exist; if the result is truncated, metadata options are used instead |
| group | "Count by Status" | Option-set and lookup fields only |
| related | "Open opportunities of these accounts" | Common 1:N child tables from metadata (opportunity, incident, contact, salesorder, contract) |
| time | "Last 30 days" | `createdon` only |
| sort | "Largest 10 by Amount" | Numeric/money field; offered when the result has more than 10 rows or is truncated |

Exclusions: fields already filtered, system, key and name fields, and fields or tables closed by the data access list are never suggested. Aggregates, chain answers, summaries, drafts and zero-row results get no suggestions.

- **Direct kinds** carry an id with parameters (`narrow:attr:value`, `group:attr`, `sort:attr`, `time:createdon:30d`). The client sends only this short string; the server parses it and validates it against the schema (field exists, not closed, type fits, option value listed, numbers in a fixed range).
- **Related** suggestions carry a full question and are sent as an ordinary follow-up question (using the previous result as context).
- After a group suggestion is applied, the aggregate answer opens with the chart view (`preferChart`).

## Design
- `Application/Suggestions`: `SuggestionBuilder` (pure: FetchXML + `EntityInfo` + result table to suggestions), `SuggestionTransform` (id to FetchXML change), `SuggestionService` (`ForAsync` never breaks the answer; `ApplyAsync`).
- Transformations: narrow adds `condition eq <value>` to the root filter (an `or` filter is wrapped in `and`); time adds `createdon last-x-days N`; sort adds `order desc` and `top 10`; group turns the list query into an aggregate: columns, `distinct` and `_rowid` are dropped, a `groupby` on the field plus a distinct `count` on the primary id are added, ordered by count descending; link-entity filters are kept and link columns dropped.
- The apply flow matches filter-chip editing: `POST /api/result/{id}/suggest/apply {suggestionId}` checks ownership (otherwise 404), loads the stored FetchXML, transforms it, validates with `PlanValidator`, executes with the caller's permissions through `AskGuard` (quota and time limit apply), and returns a new result (parent: the old result, same chat, question text suffixed with "(then: <label>)"). It is not counted as administrator-viewable. Status codes: 404, 422, 403, 503.
- Answers expose `AnswerOutcome.Suggestions` and DTO `suggestions[{id, kind, label, question}]`. Labels are localized on the server in the request language.
- The server still returns `narrowHints` for the result page, which the widget no longer renders.

## Measurement
`suggestion_stats(day, kind, shown, clicked)` stores counts per kind only (no text, fields, values or users), is purged with `Metrics:RetentionDays`, and is shown on the dashboard. "Shown" counts only fresh answers, not reopened results. Clicks on related suggestions are reported through `POST /api/suggestions/click {kind}`. A stats failure never breaks the answer.

## Out of scope
Model-generated suggestions, free-text or city narrowing boxes, suggestions on aggregates, multi-field grouping, suggestions on the result page.
