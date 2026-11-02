# Starter Acceptance Set from the Schema

Status: Implemented.

## Summary
An integrator should not have to write test cases by hand to check that the tool works on a given CRM. The generator reads the CRM schema and produces a starter acceptance set of roughly 30-50 cases. Each case's correct answer (a reference FetchXML) is built by code, with no model and no manual writing. Cases run in reference-query mode (`reference-query-evaluation.md`), so they stay valid when data changes.

## Goals
- Generation from the command line, reusable from the admin panel (`quality-checks.md`): the generator is a pure Application-layer component (schema and facts in, cases out); the CLI is a thin shell.
- Question text in the customer's language (`tr` or `en`).
- Respects the data policy: the generator sees the policy-filtered schema, so closed tables, closed fields and `SensitiveNames` fields never appear in a case.
- Generated cases are marked unverified.

## Design rules
- **Unverified until approved.** Every case has `"verified": false`. Unverified cases run but are excluded from the pass rate and reported on a separate line ("UNVERIFIED generated cases x/y"); failures still print. A consultant marks cases verified or deletes them.
- **No low-signal cases.** A reference that returns nothing, or the whole table, is not generated.
- **No leakage.** Generated sets are for evaluation only; they never enter plan examples, the knowledge base or the plan cache.
- Only single-table cases are generated; multi-step cases come from consultants or "Add to acceptance set".

## Inputs and facts
- Input: filtered `EntityInfo` list (labels, field types, option labels, relationships) and a language.
- Facts, gathered under the caller's identity with read-only aggregate queries: record count per entity; per option field, how many records each option has; count of populated records per numeric field (using a not-null filter, because counting a money column directly fails); group counts per lookup.
- Entity selection: tables open in Data access that have records, up to `--max-entities` (default 6) by record count; system tables, N:N intersect tables and activity-style tables are excluded; the table must have a primary name field.

## Templates (up to 7 per entity)
1. **Count**: "How many {plural label} are there?" (aggregate count).
2. **Small-table list**: when the table has at most 200 records, list it (name column, unordered multi-set).
3. **Option filter**: for the top 1-2 options by record count, "{field label} {option label} {plural label}" (`eq`, 1-200 rows).
4. **Option breakdown** (chart-friendly): "{plural label} by {field label}" (aggregate with `groupby`; `compare.columns: last`; at least 2 categories).
5. **Top 5**: a numeric/money field with at least 10 populated records (`order desc`, `top 5`, `not-null`, `ties: boundary`).
6. **Bottom 3**: same field, ascending (exercises the null-exclusion rule).
7. **Group by lookup**: count per N:1 lookup (`columns: last`).

Excluded from candidate fields: system, sensitive, technical and derived fields (for example discount and tax amounts), and fields with ambiguous duplicate labels (two fields sharing a label are skipped). Tables over 500 records skip the list template. There is no date template because relative dates drift.

## Output format
`eval/generated/<name>.json` in the regular case format (`id` `gen-<entity>-<template>-<n>`, `question`, `expect {finalKind, entity, reference{fetchXml, compare}}`) plus `generated`, `verified`, `template`, `note`. The directory is customer-specific and not committed. If `eval/generated/lab-tr.json` exists, `tools/Run-Regression.ps1` runs it as the "generated lab set" suite; otherwise the set is run by hand.

## Commands
```
dotnet run --project src/CrmNlQuery.Eval -- --generate-set --entities account,contact,incident --lang tr --policy eval/policy-lab-open.json --out eval/generated/lab-tr.json
dotnet run --project src/CrmNlQuery.Eval -- --cases eval/generated/lab-tr.json
dotnet run --project src/CrmNlQuery.Eval -- --list-generated eval/generated/lab-tr.json
dotnet run --project src/CrmNlQuery.Eval -- --verify-generated eval/generated/lab-tr.json --ids a,b,c
```
`--list-generated` prints question and reference summaries for review; `--verify-generated` marks the chosen ids (or `all`) verified. Without `--entities`, the automatic selection above applies. `--policy` takes a policy file that filters the schema the way the service does.

## Quality bar
A generated set is expected to pass at a high rate with a capable model. A low rate points at ambiguous question templates (fix the template, not the reference). Cases whose chosen values later disappear become LOW-SIGNAL rather than failing.

## Future work
Date templates, multi-step and chain generation.
