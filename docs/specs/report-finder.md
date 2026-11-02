# Report Finder

Status: Implemented.

## Summary
Users can ask the chat to find and open the CRM's own SSRS reports ("open the Neglected Cases report", "account summary report", "show reports"). The service answers with a report card whose link opens the CRM report viewer. No report data reaches the service; the CRM enforces permissions.

## Behavior
- The question path catches the request by rule, without a model, before the normal pipeline. Intent = a report word (report/reports and Turkish "rapor" forms) plus an open/show verb, or a short phrase ("X report"). Counting or ranking language ("how many", "total", "most") cancels the intent so data questions are not captured.
- Matching: meaningful words (filler such as report/open/show is dropped; diacritic-folded; a prefix of at least 4 letters tolerated) are compared with report **name** and **description**; name matches weigh more than description matches; at least 60% of the words must be found. At most 5 reports are listed; with no meaningful words (for example "show reports") the first 10 reports by name are listed. If nothing matches, the question continues into the normal pipeline instead of being swallowed.
- The answer is a card: report name, short description and an **Open report** link (a new tab in the CRM viewer, `crmreports/viewer/viewer.aspx?action=run&id=...`; parameters are chosen there). Only http/https addresses become links.
- Only top-level reports are listed (`parentreportid` null) of type RDL (1) or web link (3). The list is read with the caller's own permission, so reports the user cannot see are not listed. It is cached per user for 5 minutes and recorded in the access log as "other".
- The card is stored in the chat as a read-only entry (`history.kind='report'`; payload: report id, name, description, address; no customer data) and reopens from the stored entry without a CRM call.

## Report catalog
Administrators with `Reports.Manage` maintain a catalog (admin page "Reports", table `report_catalog`, at most `Reports:MaxEntries` entries):
- An entry is either an **alias** of a CRM report (display name, description, keywords) or an **external address** (http/https, no user info) for SSRS reports not uploaded to the CRM.
- Keywords count like the name for matching. An alias appears only if the user can see that CRM report; external addresses are offered to everyone and work even when the `report` table is closed.
- **Record context:** on a form, if the entry is bound to that entity, the report opens for that record: for CRM reports with `&context=records&recordstype=<ObjectTypeCode>&records=%7b<id>%7d`, for external addresses by appending the id under the administrator-defined parameter name. The card shows "Record: <name>".
- A "Try a question" card lets the administrator test matching. Changes are audited (`reports.create|update|delete`).

## Usage counter
`report_stats` stores per report name only the number shown and opened (no user and no question). `POST /api/reports/click` is accepted only from the card's owner for a report present on that card. `GET /api/admin/reports/summary` feeds a dashboard box.

## Security and configuration
- The `report` table is **closed by default** in the data access list (it is not among the default tables). An administrator opens it in Data access; while closed, the CRM-report part of the feature is silently inactive and the question goes to the normal pipeline. The service account also needs read access to it (`Setup-ServiceAccount-Crm.ps1 -ReadTables <existing>,report`), because impersonation checks privileges on both the user and the service account.
- `Features:Reports` (default on). No model call is made; the feature does not count against the question quota or model metrics.

## Out of scope
Filling report parameters from natural language, creating reports with a report wizard, querying SSRS SQL-filtered views. Report names are matched only in their own language; catalog keywords cover other languages.

## Print and copy
The result page has a print layout (all rows, print stylesheet) next to the existing "copy FetchXML" action.