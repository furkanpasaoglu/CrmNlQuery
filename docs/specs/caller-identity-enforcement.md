# Caller Identity Enforcement

Status: Implemented.

## Summary
"Who is asking?" is enforced by structure, not discipline. Running a CRM data query without a caller is impossible at compile time and test time, and every `/api` endpoint must declare how it is protected.

## Goals
- No code path on the production data route can query the CRM with the service account's own rights.
- No `/api` endpoint can be mapped without an explicit access declaration.
- User-visible behavior is unchanged.

## Design

### The `Caller` type (Domain/Access)
`Caller.User(Guid id)` (an empty id is rejected) and `Caller.ForEvaluation()`; properties `IsEvaluation` and `UserId`. The query executor, record searcher, record counter, record-context resolver, `AskGuard` and the ask request take a required `Caller` instead of `Guid?`; there is no null path. `CallerResult` carries a `Caller?`.

### Two distinct service-account routes
- **System reads** (schema, default view columns, CRM user search, user names in the panel): `CrmClient.GetAsServiceAsync(url, ct)`. It never reads row data of business tables. `ServiceReadPolicy` restricts it to a short allow-list (WhoAmI, entity definitions and typed attributes, saved queries with `$select` and `$top` of at most 10, a limited systemusers shape, and the narrow reads used by sign-in) and refuses everything else before a request is sent: business-table addresses, a `fetchXml` parameter, absolute, parent-directory or percent-encoded addresses.
- **Evaluation caller** (`Caller.ForEvaluation()`): for the evaluation tool and tests only. The executors accept it only when built with `allowEvaluationCaller` (on in `EvalComposition` and test setups, off in `AppComposition`); otherwise it throws `InvalidOperationException` and logs.

`CrmClient.GetAsync(url, Caller, ct)` sets `MSCRMCallerID` for a user and no header for the evaluation caller. `SendJsonAsync` already requires a user.

### Endpoint markers
Route-handler extensions `RequireCaller()` (CRM user: ask, results, chats, preferences), `RequireAdmin(permission)` (panel, via `AdminAuthorizer`) and `Public(reason)` (deliberately open). Each adds metadata and an endpoint filter that runs the gate itself, so a marker cannot lie. The resolved `Caller` / `AccessPrincipal` is stored in `HttpContext.Items` and read with `http.Caller()` / `http.Admin()`; identity is resolved once per request.

Safety net: middleware answers 500 to any request that matches an unmarked `/api` endpoint, so a forgotten marker fails closed. CORS preflight and static files are outside `/api`.

Response codes for `RequireCaller`: 401 when the identity or CRM user cannot be established, 503 when the CRM is unreachable.

## Tests and rules
- Endpoint inventory: every mapped `/api/*` endpoint carries a marker.
- Unauthenticated-request table: each `RequireCaller` / `RequireAdmin` endpoint refuses a request without identity.
- Architecture tests: Web cannot reference the evaluation caller or `GetAsServiceAsync`; ports contain no `Guid? caller*`; a new `GetAsServiceAsync` call site is caught.

## Out of scope
The permission model, audit of denied actions ([Denied-action audit](denied-action-audit.md)), the `App.Use` gate ([Group mapping and App.Use](group-mapping-and-app-use.md)), and the service account's own CRM privileges.
