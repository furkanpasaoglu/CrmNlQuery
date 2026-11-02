# Data Access Policy (Table and Field Allow-List)

Status: Implemented.

## Summary
Without a policy, a user's effective access is whatever the CRM allows. An administrator can add an allow-list on top: the tool never touches closed tables and fields, even if the user's CRM role would allow them. Effective access is the intersection of the CRM permission and the allow-list; the list never widens access.

## Goals
- Closed tables and fields are never shown to the model and cannot be used anywhere in a query: columns, filters, ordering, grouping, aggregates, counts, joins.
- Closed data does not appear in results, explanations or "Open in CRM" views created by the tool.
- The list is managed from the panel behind its own permission, and every change is audited.

## Design
### Single enforcement point
The schema reaches the model, the validator, the explainer, the counter and the view builder through `IMetadataSource`. `PolicyMetadataSource : IMetadataSource` wraps the real provider in the service composition (evaluation and tests use the unfiltered schema):
- `ListEntitiesAsync` returns only open tables; `GetEntityAsync` returns `null` for a closed table.
- Closed fields are removed from `Attributes`. Relationships and N:N relationships pointing to closed tables or fields are removed. An N:N intersect table between two open tables is implicitly open unless an administrator closed it explicitly.
- Lookup columns that point to a closed table, and derived `<field>name` / `yominame` / `type` columns, are hidden. Owner and creator columns (target `systemuser`, `team`, `owner`) stay available as names even when the target table is closed.
- Because closed data is absent from the filtered schema, the model never sees it, and `PlanValidator` rejects it as an unknown field in every position, including `condition@valueof` column-to-column comparisons and `order` by attribute.
- The primary id and primary name field of an open table are always open and locked in the panel (needed for `_rowid`, relationships and name resolution).

### Places where filtering alone is not enough
1. Stored queries: `ResultService.ExecuteStoredAsync` runs `StoredQueryCheck` (a light scan of every table and field against the filtered schema) before executing. If the query uses closed data the executor is not called and a `DataPolicyException` produces the fixed message "This result uses data that is no longer available." (HTTP 403 when reopening a result or editing an explanation). Result rows are never stored, so old rows cannot leak.
2. Default columns of a table (the CRM default view) are intersected with the allowed fields.
3. Record name resolution and form context are not performed for closed tables.
4. Plan and analysis cache keys include the allow-list version, so changing the list invalidates cached plans.
5. `PreviousQueryGuard` drops the follow-up context when the previous query used closed data.
6. When a question needs a closed concept, the planner is told to answer `cannotAnswer` instead of substituting another field (outcome kind `no_matching_field`). The rule applies only to a missing concept, never when the question names a record.

## Defaults
- **Tables:** with no stored decision a table is open if it is in the starting list (`DataPolicy:DefaultTables`, otherwise the built-in eight: `account`, `contact`, `opportunity`, `product`, `contract`, `contractdetail`, `incident`, `salesorder`) and closed otherwise. A table the service account cannot read cannot be opened.
- **Fields:** open by default, except fields matching the sensitive-name rule, which are closed automatically and listed for review. The rule works on logical name and label, accent- and case-insensitive (`NameNormalizer.Fold`): long words match as substrings (for example identity, IBAN, salary, credit, birth date, passport), short tokens (`tc`, `tckn`, `tcno`, `tcid`, `ssn`) only as whole words, and labels with spaces ("Tax ID") are also tried. The rule is a suggestion, not a guarantee: a sensitive field whose name does not match stays open, so installations holding sensitive data should review all fields.
- An explicit administrator decision always overrides the default.

## Storage
`IDataPolicyStore` (in-memory and PostgreSQL `data_policy`) holds rows of `kind` (`table` | `field`), `entity`, `attribute`, `allowed`, `set_by`, `set_at`. No row means the default applies. `DataPolicyService` caches a snapshot for 30 seconds and is invalidated immediately on change on the same server. If the store has never been read successfully it fails instead of falling back to defaults (administrator closures must not be lost); if a later refresh fails it keeps the previous snapshot. The in-memory store logs a startup warning and the panel shows a warning, because decisions would be lost on restart.

## Panel
Page "Data access" (permission `DataPolicy.Manage`, held by Administrator):
- Tables with label, logical name, state, source (starting list / administrator decision / default closed), counts of automatically and manually closed fields, and open/close actions. Opening first runs a readability check with the administrator's identity (`top=1` on the primary id; no data is returned or shown; logged to the access log as `other`); an unreadable table cannot be opened.
- Fields of the selected table with search, an "automatically closed" filter, badges and lock icons, plus a review list of all automatically closed fields.
- Changes are audited as `datapolicy.table` / `datapolicy.field`; the master switch is audited as `datapolicy.switch`.
- The page and the setup checklist state plainly that the list applies to the chat tool only. In an "Open in CRM" view a closed field is not a column, but the user can add it inside the CRM; the CRM's own field-level security remains the control there.

## Settings
| Key | Meaning |
|---|---|
| `Access:DataPolicy` | Master switch, `on` (default) or `off` (emergency only). Changing it requires `DataPolicy.Manage`. |
| `DataPolicy:DefaultTables` | Starting list of open tables. |

## Out of scope
Per-role or per-user policies; replacing CRM field-level security; knowledge-base documents; scanning row contents for sensitive data.
