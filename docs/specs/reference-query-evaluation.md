# Reference-Query Evaluation Mode

Status: Implemented.

## Summary
Eval cases used to hard-code the expected row count or names, so any data change broke cases without any model error. Reference-query mode stores a verified FetchXML per case instead. The runner executes both the candidate's query and the reference query on the data as it is now, under the same identity, and compares the result sets. Cases without a `reference` keep working unchanged and the two styles can be mixed.

## Goals
- The eval measures the same thing when data changes; "model wrong" is separated from "data changed".
- Flaky cases are visible as counts.
- Only the eval tool changes; the service is untouched.

## Design rules
- Result sets are compared, not query text.
- The comparison rule is chosen per case.
- If the reference contains `<order>`, order matters; otherwise it does not. Duplicates compare as a multi-set, because join duplication is a real error class. NULL never equals 0.
- Weak references (zero rows, or the whole table) are flagged "LOW-SIGNAL"; structural checks (`mustUseFetch`, `mustNotUse`) still apply.
- Candidate and reference run with the same `Caller` close in time (`EvalComposition.CreateReferenceRunner`, `Caller.ForEvaluation`).
- Reference queries never enter plan examples, the knowledge base or the plan cache; eval already runs with those caches off.

## Case format
```json
"expect": {
  "finalKind": "answer", "entity": "opportunity",
  "reference": {
    "fetchXml": "<fetch>...</fetch>",
    "compare": {
      "order": "auto",
      "duplicates": "multiset",
      "columns": "reference",
      "tolerance": 0.01,
      "ties": "strict"
    }
  }
}
```
- `"file"` may replace `fetchXml` for long queries; `"steps"` defines a multi-step reference.
- `order`: `auto` (ordered if the reference has `<order>`), `ordered`, `unordered`. `duplicates`: `multiset` or `set`. `columns`: `reference` (the reference's columns must exist in the candidate, extras allowed), `exact`, or `last` (only the final measure column is compared, for grouped results whose key format may vary). Candidate columns match reference columns by displayed header; if no header matches but column counts are equal, columns match by position.
- `tolerance`: absolute tolerance for numeric columns (aggregate results compare raw values). `ties`: `strict`, or `boundary` (at a top-N cutoff any tied member is accepted).
- Cell values come from the same `ResultTableBuilder` pipeline for both sides.
- Multi-step references: each intermediate step has `reduce: min|max|all` (ties included), and the next step uses `{{ids}}`. If the first step is empty, the reference is empty. An empty reference requires an empty candidate.
- Reference results are capped at 500 rows (the product limit). A reference over the cap is "limited": only the total count and the ordered first 500 are compared; an unordered reference over 500 rows produces a warning ("add order").
- Every `link-entity` in a reference must carry an alias, because the table builder requires it.

## Data pre-checks
- A case may declare `requires: [{ "entity": "account", "name": "<record name>" }]`. All are looked up in one pass at the start; cases with missing records are reported `DATA-MISSING`, not FAIL, and excluded from the pass rate.
- `eval/data-fingerprints.json` stores per reference the row count and a digest. A different fingerprint does not fail the case (both sides ran on the same data) but prints `DATA-CHANGED (reference 4 -> 14 rows)`. `--accept-data` records new fingerprints.

## Report
- Per suite: passed/failed plus counts of DATA-MISSING, DATA-CHANGED and LOW-SIGNAL.
- With `--repeat N`: pass rate per case; FLAKY means a rate strictly between 0 and 1; a regression means 100% in the previous results file and below 100% now.
- Failed reference cases show the difference (up to 10 missing or extra rows) and the candidate's FetchXML.
- `tools/Run-Regression.ps1` summarizes these.

## Design notes
- A wrong reference makes a case wrongly pass or fail, so each reference is checked against the data it was written for and read by a person.
- Columns default to "reference columns must be present, extras allowed", because models tend to add columns; strict cases use `exact`.
- Ties default to `strict`; tie-sensitive cases use `boundary` or their own rule.

## Future work
Remaining fixed-count cases migrate gradually. Starter-set generation (`starter-acceptance-set.md`) and the admin panel (`quality-checks.md`) build on this mode.
