# Denied-Action Audit

Status: Implemented.

## Summary
The audit log records successful administrative changes. This spec adds two records: refused attempts to use the admin endpoints, and CRM metadata refreshes.

## Behavior
1. **Denied admin attempt (known identity).** When the admin gate refuses a user whose account is known, an audit entry is written: `Action = access.denied`, `Actor = account`, `Target = "<METHOD> <route template>"` (no query string, no values), `Detail = "needs <permission>; <reason>"`. Reason is `no_access` (not registered, inactive, or no role) or `missing_permission` (registered but lacks this permission).
2. **No identity.** A request without an account is written to the application log only (Warning, endpoint name). It never goes to the audit table, so anonymous traffic cannot inflate it.
3. **Undecidable.** When the permission store is unavailable (503) only the application log gets a Warning; reporting "denied" would be wrong.
4. **Metadata refresh.** `POST /api/admin/metadata/refresh` writes `Action = metadata.refresh`, `Actor = account`, `Target = "crm metadata"`, `Detail = ok | failed`. The CRM error message is not recorded.
5. **Flood control.** The same (account, endpoint, reason) is recorded at most once per 10 minutes, using an in-memory counter (single server). When the counter suppresses a record nothing is logged.
6. **Audit failure never changes the response.** The refusal is still 404/401/503; a write error is logged as a Warning.

Refusals on question and result endpoints (`RequireCaller`) are application-log only; they are not administrative actions.

## Design
- `AdminAuthorization` carries a reason: `Allowed`, `NoAccess`, `MissingPermission`, `Unavailable`. The externally visible 404/503 behavior is unchanged.
- `AccessService.RecordDeniedAsync(account, endpoint, permission, reason)` applies the 10-minute counter with an injectable clock and writes through `AddAuditAsync` (generalized as `RecordAccessAsync`, which also writes `access.would_deny`).
- `AdminAuthorizer` records on refusal when an account exists, otherwise logs. The endpoint name comes from the route template of `HttpContext.GetEndpoint()`.
- No schema change: the existing audit table (time, actor, action, target, detail) is used. The audit page lists the new actions without new filters.

## Security and privacy
- Responses to the refused caller stay identical (no extra information).
- Question text, query content and row data never enter the audit log.

## Out of scope
New audit-page filters; audit of refusals on user-facing endpoints.
