# Record Creation from Chat

Status: Implemented; off by default.

## Summary
A user can create new CRM records from the chat. The model only proposes values; validation and the write happen in code, and nothing is written until the user explicitly clicks Create (human-in-the-loop approval gate).

## Scope
1. Single record: "create an account named Alpine" -> missing required fields are asked for -> preview -> Create / Cancel -> written to the CRM.
2. Pasted JSON (one object or an array): no model involved, validated against the schema, missing or wrong fields are reported.
3. Multiple records (a list or a text table): the model turns the text into rows; a preview table shows per-row status; only valid rows are written, after confirmation.

Out of scope: update, delete, Excel/CSV upload, related-record chains, multi-select/file/image fields.

## Flow (single record)
1. `CreateIntent` (rule based: a verb such as create/add/new/save plus a writable table name or label) is checked at the very start of the question pipeline.
2. `CreateExtractor` (model, structured output) receives only the field lists of writable tables and returns the table plus field/value pairs. The model cannot invent fields: an unknown field is reported as an issue.
3. `CreateValidator` (code) converts every value to the field type (text length, number, date, yes/no, option label, lookup = record-name resolution) and checks required fields (metadata `ApplicationRequired` / `SystemRequired`).
   - Missing required fields or issues -> outcome `ask`: fixed, model-free messages tell the user what is needed. A pending draft is kept on the server for 20 minutes.
   - When the user answers (the widget sends `draftId`), the model extracts only the new values, merges them into the draft, and the draft is validated again. If no value can be extracted the message is treated as a normal question and the draft is dropped.
   - Complete -> outcome `preview`: a field/value table plus a duplicate warning (same name already exists; read with the caller's permissions, first 3 matches).
4. Create (`POST /api/create/{id}/confirm`): the draft is bound to its owner; it is re-validated (table and field permissions still open), quota and row limits are checked, and the record is written as the caller (CRM impersonation header). The result is `done` with an "open record" link. Cancel (`POST /api/create/{id}/cancel`) deletes the draft.
5. The final state (created / cancelled) is stored in the chat as a read-only card (`history.kind='create'`: summary only; table, record name and id, no field values).

Outcome type `CreateOutcome` has kind `ask | preview | done | cancelled`.

## Security and privacy
- The master switch `Features:RecordCreate` is off by default. Writable tables are listed in `Create:Tables`; a table must also be open in the data access policy, and closed fields are absent from the schema.
- Permission: `Records.Create` plus the CRM's own create privilege, which the CRM checks on both the impersonated user and the service account. Granting the service account create privilege on the listed tables is a deployment decision.
- Create only. The model never writes rows or fields directly. Every value is validated by type. Id, owner, state and system fields are not in the schema.
- Limits: at most `Create:MaxRows` records per request and `Create:PerDay` per user per day.
- Duplicates: an existing record with the same name produces a preview warning and the button becomes "Create anyway".
- Audit: `record.create` (who, table, record count, record ids; no values). The data access log also records the write with source `create`.
- Pasted text may contain instructions (prompt injection). The model only converts text to rows, output is validated in code, and no write happens without human confirmation.
- Pasted data is sent to the configured model.

## Design
- Schema: `CreateSchema` (table, collection name, primary id/name, fields: name, label, type, required, length, options, lookup targets, navigation property). `CrmCreateSchemaProvider` reads entity definitions with the service account (allowed by `ServiceReadPolicy`) and caches them in memory for 15 minutes.
- Writer: `CrmRecordWriter` issues `POST {collection}` through `CrmClient.SendJsonAsync` (caller id mandatory); lookups are written as `navprop@odata.bind`.
- Draft store: `ICreateDraftStore` (in memory, TTL 20 minutes, at most 5 per user). Pending drafts are lost on restart.

## Settings
| Key | Default | Meaning |
|---|---|---|
| `Features:RecordCreate` | off | Master switch |
| `Create:Tables` | empty | Comma-separated writable tables |
| `Create:MaxRows` | 50 | Records per request |
| `Create:PerDay` | 200 | Records per user per day |

Permission: `Records.Create` (included in Administrator).

## Out of scope
Updates, deletes, spreadsheet upload, multi-record correction of a single row in the preview (rewrite the request instead).
