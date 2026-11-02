# Open in CRM

Status: Implemented; off by default.

## Summary
When a result is truncated (the service returns at most 500 rows), the user can open the same query in the CRM's own list view. The CRM then provides paging, sorting, filtering and (under its own permissions) export, and works on very large tables. The service's result page stays as a fallback.

The feature **writes** to the CRM (one personal view per user), which breaks the otherwise read-only nature of the tool. It is therefore behind a switch that is off by default.

## Goals
- One click opens a large result in a CRM view, with no extra questions.
- Missing permission or any CRM error falls back silently to the existing result page link.
- Administrators can switch the feature on and off at runtime.

## Behavior
- **Switch:** `Features:OpenInCrm` (boolean, default `false`, changeable at runtime). When off, the endpoint behaves like a 404 and the widget never shows the button.
- **Permission:** `Records.OpenInCrm` is granted per user through roles (Administrator holds it implicitly). Without it, answers carry no `openInCrm` flag and both endpoints return 404.
- **Button:** shown next to "Open all N rows" when the answer is truncated, is not an aggregate, has a result id, and the switch and permission allow it (`openInCrm: true` in the answer JSON). Clicking asks nothing: the server writes the view and the widget opens the CRM address in a new tab.
- **One temporary view per user:** each user has at most one personal view named `NL Query - last result`, overwritten on every use. The question text is never written to the view, because the view is visible in the user's CRM list and can be shared.
- **Lookup:** the view is found by name **and** owner (`userqueries?$filter=name eq '...' and _ownerid_value eq {user}`). Searching by name alone is unsafe because a personal-view query also returns views shared by others. If several matches exist (concurrent clicks) the first is used and the extras (same owner, same name) are deleted.
- **Validation before writing:** the stored FetchXML is re-validated by `PlanValidator` against current metadata, on the original query (`top` up to 500), before any paging attribute is stripped. A query that fails validation is never written.
- **Query transformation:** `top`, `count`, `page` and `paging-cookie` are removed; filters, relationships and ordering are kept. Every `order` is written with an explicit `descending="false|true"`, because the CRM view grid shows an `order` without it as descending.
- **Columns:** the root entity's attributes in order, then `link-entity` columns as `alias.attribute` cells, only for N:1 relationships (a link whose `from` is the linked entity's primary key). A query that selects columns from a 1:N relationship cannot be a view, so the button is not offered. A `link-entity` without columns (filter only) works in both directions. If the entity type changed (`returnedtypecode` cannot be updated), the old temporary view is deleted and a new one created.
- **Permissions and errors:** the view is created or updated on behalf of the user (`MSCRMCallerID`), so the CRM applies its own security roles. Any CRM error (for example a missing create-view privilege) makes the widget report that it could not open in the CRM and leaves the result page link available. Cached privilege information is only a hint for hiding the button, never a guarantee.
- **Deletion rule:** the service deletes only a view whose name equals the fixed name and whose owner is the caller. Triggers: the user deletes all history, or the entity type changes. Switching the feature off deletes nothing automatically.
- **Address:** `{CRM root}/main.aspx?etn={entity}&pagetype=entitylist&viewid={id}&viewtype=4230`, without an `appid`.

## Design
- Application: `OpenInCrmService` (switch, ownership and eligibility checks, query transformation, column and layout generation as pure functions), `ViewBuilder`, `ViewLayout`, port `IUserViewStore` (upsert and delete on behalf of the user).
- Infrastructure/Crm: `CrmUserViews` (POST/PATCH/DELETE with a mandatory caller id; the entity's `ObjectTypeCode` comes from cached metadata).
- Web: `POST /api/result/{id}/open-in-crm`, returning `{ url }` or an error (403 no permission, 502 CRM error, 404 off, not found, not the owner or not allowed). The same mechanism serves summary sections through `POST /api/result/{id}/section/open-in-crm`.
- CORS allows only the configured CRM origin, and the cross-site guard covers the write path.

## Security and privacy
- The service account needs create/read/write/delete rights on user queries only if the feature is used (`Setup-ServiceAccount-Crm.ps1 -OpenInCrm`); impersonation checks the privilege on both the user and the service account.
- Question text, FetchXML and CRM ids are not written to logs or metrics; only counts and result types are.

## Out of scope
Permanent saved views, relative-date conversion, aggregate queries (cannot be views), intermediate steps of conditional plans, charts or dashboards in the CRM.
