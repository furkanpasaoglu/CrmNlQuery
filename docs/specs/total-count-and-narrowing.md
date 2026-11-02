# Total Count and Narrowing Suggestions

Status: Implemented.

## Summary

When a result is truncated at 500 rows, the user sees how many records exist in total ("Showing the first 500 of 48,312 records") and clickable suggestions for narrowing the question. Result rows are never stored and never sent to the model.

## Total count

- The answer is shown immediately with a truncation warning. For a truncated answer the widget and result page then call `GET /api/result/{id}/count`, which returns `{ total, limitExceeded }`, and replace the warning text with the count: "Showing the first 500 of **N** records", or "... of more than **N** records" when `limitExceeded`. If the count cannot be obtained (CRM error, timeout), the original warning stays and the answer is unaffected.
- The endpoint requires an identity and checks that the result belongs to the caller (a foreign or missing result is 404; an aggregate result is 422; no count available is 503). It is a read endpoint reachable from the widget's CRM origin.
- `CountQueryBuilder` derives the count query from the stored FetchXML: the original query is re-validated by `PlanValidator`, then `top`, `order`, and all `attribute` elements (including those in links) are removed; filters and links are kept. It runs under the caller's CRM permissions. The query is produced by code, not by the model.
- Two-tier method:
  1. **Aggregate:** `aggregate="countcolumn" distinct="true"` on the root primary id. One query, correct beyond the 5,000 mark of the paging count, up to the CRM's own aggregate limit, and it counts distinct root records across a one-to-many join.
  2. **Fallback**, if the aggregate fails with any CRM error other than a permission error (for example the aggregate limit was exceeded): `distinct="true" count="1" page="1" returntotalrecordcount="true"`. This gives the exact count up to the CRM's paging-count limit; when the CRM reports `limitexceeded`, the returned number is shown as "more than N". If the fallback also fails, no count is shown.
- The number is never hard-coded; the CRM's returned value is used, and the limit-exceeded condition is separate from errors. Permission or network errors fall back to the plain warning.
- The count has its own timeout, `Counting:TimeoutSeconds` (default 15), so a slow count on a very large table never delays the answer.
- The count means distinct records of the root entity. In the rare query that selects columns from a one-to-many relationship, the table can show more rows than the count; the label says "records".
- Nothing is persisted: the number is not stored or cached, and logs and metrics record only that a count was requested or failed.

## Narrowing suggestions

- A truncated answer carries `narrowHints` (at most 3 fields), computed from metadata without a model.
- Selection from the root entity's suitable attributes, in order: (1) option/state fields (state, status, picklist, boolean; at most 8 options), `statecode` first; (2) city-like text fields (for example `address1_city`); (3) date fields (`createdon`, close date). Identity and name fields, system fields (`versionnumber`, `*_base`, `*name` shadow fields), and any attribute already filtered in the query (including those in links) are never suggested. `NarrowHintBuilder` implements this.
- Option/state hints show labels from the metadata (for example `Status: Open · Won · Lost`, at most 5 values). Clicking sends a complete question ("Of these, only those with status Open") through the normal follow-up flow (same conversation, previous query as context).
- City and date hints are a single label; clicking writes a template into the input ("Of these, only those with city ") and places the cursor, and the user types the value.
- With no suggestions, the section is hidden. Suggestions are computed on the server for each answer and not stored.
- Label and template sentences live in the widget's text table; field and option names are the CRM's own labels.

## Architecture

- Application: `CountQueryBuilder` (pure), `NarrowHintBuilder` (pure), port `IRecordCounter` (`CountAsync(entitySet, fetchXml, caller)` returns total and limit flag), `ResultCountService` (owner check, two tiers, timeout), and `AnswerOutcome.NarrowHints`.
- Infrastructure/Crm: `CrmRecordCounter`.
- Web: `GET /api/result/{id}/count`, `OutcomeDto.narrowHints`; the widget and `result.js` write the count and render the labels.

## Security and privacy

The count and the hints are derived from the stored query and metadata, run with the caller's permissions, are owner-bound, and expose no row data.

## Settings

`Counting:TimeoutSeconds` (default 15).

## Out of scope

Server-side paging, and an exact model-free filter path for suggestion clicks (they currently use the normal follow-up flow).
