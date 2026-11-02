# Customization Design Assistant

Status: Implemented.

## Summary
During requirements analysis, a CRM consultant, analyst or administrator describes a new business need ("track dealer service appointments: vehicle, customer, date, status, several operations"). The assistant inspects the existing schema and produces a design document. Its most valuable part is the "does something similar already exist?" check, which prevents duplicate customizations. Nothing is created in the CRM.

## Output sections
Requirement summary; existing schema check ("extend this table instead of creating a new one"); proposed tables (logical name with publisher prefix, display name, ownership type); fields (name, type, required, option values, length); relationships (N:1, 1:N, N:N, delete behavior); view and form suggestions; security notes (sensitive fields, per the sensitive-name rule of the data access policy); open questions. The document states that it is a proposal, not a final design.

Formats: Markdown (with a Mermaid `erDiagram`), Word (`.docx`), and a structured JSON attachment (schema version, tables, fields, relationships, views; the request text is not included).

## Design
1. **Surface:** a "Design assistant" page in the admin panel, not the chat widget. Permission `Design.Use` ("reads CRM metadata only, never data, writes nothing"); included in Administrator. Notification is in-page only (a banner while the page is open, "Ready" in the job list otherwise); no e-mail or external notification.
2. **The existing-schema check is done by code, not by the model.** The model splits the request into concepts (term, TR/EN synonyms, kind: table/field/relationship). Code searches the whole schema (table names and labels, field labels, folded matching with `NameNormalizer.Fold`, stem/part matching, synonyms) and builds an evidence-backed candidate list (table, custom or system, field count, matching fields). The model then chooses among those candidates only (extend / reuse / new); a reference to a table outside the candidate list is rejected by the validator.
3. **Which schema:** the unfiltered schema (`PolicyMetadataSource.Unfiltered`), because the goal is to find an existing similar table even if it is closed in the chat tool. Only names are visible; record data is never read. Candidates that are closed to the chat tool are marked as such.
4. **Pipeline, each step a small prompt:** (a) concept extraction [model] -> (b) schema search [code] -> (c) reuse decision [model, candidates only] -> (d) design [model, structured JSON schema] -> (e) validation [code] -> on errors, up to 2 retries with feedback to the model -> (f) Markdown, JSON and docx rendering [code, templates]. The output language follows the request language (display names and descriptions); logical names are ASCII lower case with the prefix.
5. **Validator (code):** publisher prefix (guessed from the most common prefix among existing custom tables, editable), logical name format and length (<= 50), clash with existing names, allowed field types (text, long text, whole number, decimal, money, date, yes/no, option set, lookup), unique option values, relationship ends exist in the schema or the design, a note for the N:N intersect table, delete behavior defined, a primary name field per table, sensitive field names added to the security notes automatically. A design with remaining errors is still delivered, marked "Needs review".
6. **Long-running job:** table `design_jobs` (id, owner, status `queued | running | ready | failed | cancelled`, step progress, request text, result Markdown and JSON, error, timestamps); in-memory and PostgreSQL stores. Runs in the background on the server: at most 1 active job per user and 2 per server, cancellable, with an overall time limit. Jobs left `running` after a restart become `failed (interrupted)`. Model calls go through `MeteredLlmClient` (purposes `design_*`), so duration and tokens show on the dashboard. Retention is `Retention:DesignDays`. A job is visible only to its owner (administrators cannot read other people's requests); internal error details are not returned to the user.
7. **Audit:** `design.start`, `design.download` (who, format; the request text is not recorded). The request text goes to the configured model; row data never does.
8. **Word:** no dependency; a minimal `.docx` writer (headings, paragraphs, bullets, tables) built on `System.IO.Compression`, generated directly from the design object, not from Markdown.
9. **Normalization:** when a candidate table is marked "extend" but `extends` is empty, `DesignPlanner.NormalizeExtensions` sets it to the table name; "ignore" decisions are left untouched.

## Endpoints
`POST /api/admin/design` (start, 202), `GET /api/admin/design` (list), `GET /api/admin/design/{id}`, `GET /api/admin/design/{id}/download?format=docx|md|json`, `POST /api/admin/design/{id}/cancel`, `DELETE /api/admin/design/{id}`. All require `Design.Use`.

## Settings
| Key | Default | Meaning |
|---|---|---|
| `Design:TimeoutMinutes` | 15 | Overall job time limit |
| `Retention:DesignDays` | 90 | Job retention |

## Out of scope
Creating or changing tables, fields or solutions in the CRM; reading record data; form/view XML generation; use from the chat widget; workflow/plug-in design; external notifications.

## Risks
The structured-output quality of a local model on Turkish business requests is the main variable; the validator and the retry loop are the primary safeguard. Search cost on large schemas is limited by reading a table list plus details of candidate tables only (cached metadata).
