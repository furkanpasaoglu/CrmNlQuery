# Natural-Language Query

Status: Implemented.

## Summary

An analyst types a question in Turkish or English. The service understands it, resolves company and person names to real CRM records, asks a clarifying question only when needed, generates a validated FetchXML query, runs it read-only, and shows the result as a table.

## Goals

- Replace hand-built Advanced Find filters with a question typed in natural language.
- Keep all control flow in code. The model only produces a plan; validation and execution are deterministic.
- Keep data on premises: a local model, and no result rows ever sent to the model.

Target CRM: Dynamics 365 on-premises 9.0 (the 8.2 Web API is the same surface).

## Design decisions

| Decision | Reason |
|---|---|
| Local LLM behind `ILlmClient`; no cloud model by default | Data stays inside the company. A stronger local model is a settings change. |
| FetchXML as the query language (not OData) | Aggregates, link-entity joins, and date filters; analysts already know it. |
| Two-step generation: choose entities first, then generate the query | The whole schema does not fit in a prompt; a small prompt is more accurate and easier to debug. |
| The model emits a `QueryPlan`, not raw FetchXML | Multi-step and conditional plans extend the same shape (see [conditional-plans.md](conditional-plans.md)). |
| The output is either a plan or a question | One mechanism covers every "which one?" case. |
| Result rows are never stored | CRM data changes and is sensitive; re-running always returns fresh data. |
| Validation failure feeds the error back to the model for regeneration (at most 3 attempts) | Correctness matters more than speed. |

## Components

1. **MetadataProvider** reads `EntityDefinitions` (attributes, one-to-many and many-to-one relationships, option-set labels) through the Web API, caches it on disk, and supports an explicit refresh (see [schema-change-detection.md](schema-change-detection.md)).
2. **QuestionAnalyzer** (model call 1) takes the question and the entity list (logical name and label) and returns a `QuestionAnalysis`: primary entity, related entities, company/person names mentioned (`mentions`), columns requested explicitly, and whether it is a count/sum question (`aggregate`). Count questions never trigger a column question.
3. **EntityResolver** (code) binds mentioned names to real records using `like` search with normalization: case, Turkish characters, suffixes such as `A.Ş.`. Zero matches produce a question; two or more produce a "which one?" question.
4. **PlanGenerator** (model call 2) takes the question, the schema of the selected entities, and resolved record ids, and returns a `QueryPlan`.
5. **PlanValidator** (code, no model) applies the rules below. Failures return the error text to the model.
6. **ColumnPolicy** (code) asks which columns to show when none were specified. "Default" uses the entity's default system view (`savedquery`, `isdefault=true`, `querytype=0`, `layoutxml`), falling back to the id plus the primary name attribute. "Name only" returns only the primary name attribute.
7. **Executor** runs `GET /api/data/v9.0/{entitySetName}?fetchXml=...` with `Prefer: odata.include-annotations="*"` so option-set and lookup labels are returned. At most 500 rows.
8. **Web UI**: a minimal ASP.NET Core API plus a plain HTML/JS page.

## Data flow

```
question -> QuestionAnalyzer -> EntityResolver -> ColumnPolicy -> PlanGenerator -> PlanValidator -> Executor -> table
              (LLM 1)             (code)         (code, may ask)     (LLM 2)          (code)         (code)
```

`QueryPlan { steps: [ { fetchXml, runOnlyIf? } ] }`. A single-step plan is the base case; multi-step plans are covered by the conditional and dependent specs.

## Validation rules (PlanValidator)

- The XML is valid with a `<fetch>` root and only allow-listed elements: `entity`, `attribute`, `filter`, `condition`, `order`, `link-entity`, and aggregate/groupby constructs. Anything write-like is rejected.
- Every `entity name`, `attribute name`, and `link-entity` name/from/to exists in the metadata; every `condition attribute` belongs to its entity.
- A missing `top` becomes 500; a larger `top` is lowered to 500.
- A condition on an option-set attribute uses a valid value from the metadata; a lookup condition uses a GUID.
- `all-attributes` is rejected; the attribute list is always explicit.
- Every `link-entity` that contributes attributes carries an `alias`, so result column keys (`alias.attribute`) are predictable.
- Value counts: `in`/`not-in` and every value-taking operator need at least one value; `between`/`not-between` need exactly two; only `null`, `not-null`, and date shortcuts (`today`, `this-month`, ...) may take none.

## Behavior

The page shows, in order: what was understood (resolved records, entity), the generated FetchXML (read-only, with a copy action), and the result table.

- The table is dynamic: columns come from the result keys; option-set and lookup labels and link-entity fields use their aliases.
- Pagination and a search box work client-side over the returned rows. When the 500-row cap is reached, a truncation warning is shown.
- Clarifying questions appear only when needed:

| Situation | Behavior |
|---|---|
| No columns specified | Asks: default / name only / choose columns |
| Columns specified | Runs without asking |
| Name matches 2+ records | Asks which one |
| Name matches 0 records | Reports not found and suggests similar names |
| Attribute name has no exact match | Offers the closest match |
| Entity not understood | Asks |

## Security and privacy

- The model receives the question, schema metadata, and the names of candidate records found during name resolution. Result rows never reach the model.
- Queries are read-only. With a local provider nothing leaves the machine.
- Per-user execution under the caller's own CRM permissions is described in the identity and access specs.

## Evaluation

Behavior is measured with an evaluation set of question cases run against seeded test data. Invalid plans must never be sent to the CRM, and ambiguous or missing names must produce a question rather than a guess.

## Model selection

The default model is `qwen3.6:27b`, chosen for accuracy over speed. Alternatives are `qwen3:30b-a3b` (faster) and `qwen3:14b` as a minimum-hardware candidate. Structured output uses the model server's JSON-schema support. The provider sits behind `ILlmClient`, so the model can change without code changes.

## Out of scope

Chat window and history, multi-user execution, business glossary, retrieval, record creation, and result snapshots are specified separately.
