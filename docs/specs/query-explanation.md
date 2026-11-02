# Query Explanation

Status: Implemented.

## Summary
Every answer carries a one-line, expandable explanation of what the executed query actually did, in the schema's display names and without technical knowledge. The explanation is produced by code from the executed FetchXML and metadata labels, never by the model, so what is shown always matches what ran. Filters in the explanation can be removed or changed without calling the model.

## Goals
- Show entity, filters, relative dates as real ranges, related-table filters, sorting and limit, and aggregation.
- Never show raw FetchXML to normal users.
- Let users remove or change a filter chip and re-run.

## Design
- `QueryExplainer` (Application/Explain) is model-free and makes no CRM call. Inputs: the final (executed) FetchXML, `EntityInfo` metadata (labels, option labels, relationships), resolved records (GUID to display name; unknown becomes "a specific record"), and a clock. Output: a structured `QueryExplanation`. A GUID is never printed.
- The structure is language-neutral; operator wording comes from the widget's text table and dates are ISO and localized by the client. It contains the entity, filters with stable ids (`f1`, `f2`, ...), sort, top (the default 500 is hidden), aggregate measures (sum, avg, count; group labels such as "Created On (month)"), and notes.
- Filter ids are a counter in document order, regenerated from the stored query each time, so they are stable for that query.
- Relative date operators (`last-x-days`, `this-month`, `last-year`, `olderthan-x-months`, ...) are shown with the real range computed from the server's UTC date (weeks start on Sunday); the UI prefixes it with "≈" because the CRM uses the user's time zone. An unrecognized operator is shown by name; a date is never invented.
- Option-set and Yes/No values use metadata labels, lookup values use resolved record names, link-entity filters show their relationship ("Account: Segment = ..."), and `or` groups are shown as "any of".
- The explanation is attached to the answer (`AnswerOutcome.Explanation`, `OutcomeDto.explanation`) on ask, refine and result reopen. If a nested `or` filter cannot be shown faithfully, no explanation is produced and the UI falls back to the plain `understood` sentence. Explanation failures never break the answer. Chain answers show earlier steps as summaries with selected record names, never ids.
- A fixed note, "Among the records you can see", states that the CRM's own permissions apply.
- The total record count stays in the existing count line and is not copied into the explanation.

## Editing filters
- `POST /api/result/{id}/explain/edit` with `{ chipId, action: "remove" | "set", value }`; requires a caller, and the result must belong to the caller (otherwise 404).
- `ExplainEditService` loads the stored query, regenerates the explanation, finds the condition by chip id, removes or changes it, cleans up emptied filters and links, validates with `PlanValidator`, and runs it with the caller's permissions through `ResultService.ExecuteStoredAsync`. The new result is a new history entry whose parent is the old result, in the same chat, with the original question text plus a single "(edited: ...)" suffix. The model is not involved.
- Editability is computed in the explanation (`ExplainFilter.Edit`): option sets (from the option list), integers, numbers, and text (1 to 200 characters; `%` is stripped; `_`, `[`, `]` rejected in `like`, wildcard form preserved). Date, lookup, multi-value and valueless operators can only be removed.
- Rejected with 422 (`edit_not_allowed`, `edit_invalid`, `edit_unknown_filter`): aggregates, chain-derived fixed id lists, `or`-group chips, invalid values.
- Edits pass through `AskGuard` (quota, concurrency, time limit). Edited results are not counted as administrator-viewable.

## UI
The widget shows one summary line under the answer; clicking it expands one row per filter plus the permission note, with × and a change control per chip. The result page shows the same explanation. The shared rendering block is identical in `widget.js` and `result.js`.

## Security and privacy
The explanation contains only query structure, metadata labels and resolved record names the user could already see; no row data. Labels are inserted with `textContent`. The edit endpoint verifies ownership and passes the validator, so it opens no new data path; the cross-site guard covers the write path.

## Settings
`Features:ShowFetchXml` (default off): when off, `OutcomeDto` omits `fetchXml` entirely; when on, the raw FetchXML section is shown in the widget, result page and test panel.

## Out of scope
Model-written explanations, editing in multi-step chains, changing date chips, an authorization-based "advanced" FetchXML view.
