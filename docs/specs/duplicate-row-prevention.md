# Duplicate Row Prevention

Status: Implemented.

## Problem

For questions such as "companies that have opportunities", the plan uses the company as root and joins opportunities through a `link-entity`. Each opportunity yields a row, so a company with three opportunities appears three times. The validator allows `distinct`, but nothing adds it, and the total count (which counts distinct records) would disagree with the table.

## Rule

`distinct="true"` is added only when a one-to-many relationship is used purely as a **filter**, meaning the `link-entity` contributes no attributes. If related columns are requested, duplicates are the expected result and the query is left alone.

`distinct` merges rows that look identical but are different records when the primary id is not selected. The rewriter therefore always includes the root entity's primary id among the selected attributes. Rewriting without the id would silently lose records.

## Design

Code, not the model, does this, so it is deterministic and needs no prompt change. `DistinctRewriter` runs on the normalized FetchXML after `PlanValidator` succeeds.

For a single-step, non-aggregate fetch:

1. If `aggregate="true"`, do nothing. If `distinct` is already present, skip to step 3.
2. A "filter join" is a `link-entity` under the root with no `attribute` and no `order alias` anywhere inside it, and that is one-to-many (determined from metadata: `link-entity.from` is not the related table's primary id while `to` is the root's primary id; many-to-one joins do not multiply rows).
3. If at least one filter join exists and no other link-entity contributes columns, add `distinct="true"` and add the root primary id attribute if absent.
4. If any other link-entity contributes columns (mixed case), do nothing, since the user wants the related data.
5. An outer join with an `entityname` null filter (a "has none" condition) does not multiply rows; the same rule applies harmlessly.
6. If an `order` refers to an attribute that is not selected, the rewriter adds it to the selected attributes, which is safe on every CRM version.

The primary id column is visible in the table as an ordinary column; the row id also supports the per-row "Open" link.

## Interactions

- Counting (`CountQueryBuilder`) already replaces `distinct` with a distinct count on the primary id, so the table and the count agree.
- Open in CRM (`ViewBuilder`) keeps the `distinct` attribute.
- The plan cache stores the rewritten query because rewriting happens after validation.
- Follow-up questions see the rewritten FetchXML as the previous query; the prompt keeps `distinct` as is.

## Out of scope

Aggregate queries and queries that request related columns.
