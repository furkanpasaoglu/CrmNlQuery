# CRM Proof Sign-In

Status: Implemented.

## Summary
The service must know who is asking, because each user may see only the data their own CRM rights allow (queries run with the `MSCRMCallerID` header). A client claim of identity is not enough, so the CRM itself acts as the authenticator: the widget creates a one-time task in the CRM as the signed-in user, and the service reads who created it. This is independent of how the user signs in to the CRM (Windows, ADFS/IFD, or other) and does not depend on IIS or Windows authentication, so the service runs in a container.

## Flow
1. The widget generates a high-entropy `secret` (at least 43 characters, kept in memory) and calls `POST /api/auth/challenge` with it. The service creates a one-time nonce (128 bit, lifetime `Auth:ChallengeSeconds`, default 120) and stores only hashes in `auth_challenges`.
2. Using the user's own CRM session, the widget creates a task (`task`) whose subject is `NLQ-login-<nonce>`.
3. The widget calls `POST /api/auth/verify` with the nonce and the same `secret`. The service reads the task as the service account through `ICrmProofReader`, takes the creator (`_createdby_value`), and rejects the proof if any rule below fails.
4. The service consumes the nonce (a second use fails), resolves the user in the CRM through `CallerIdentityService`, reads the user's groups, and returns a signed, short-lived session token.
5. The widget deletes the task and sends the token as `Authorization: Bearer <token>` (not a cookie: the service and the CRM are different sites and third-party cookies are blocked).

## Proof rules
- The record is the creator's, not the owner's. Ownership can be reassigned; `createdby` cannot be forged. A task created on behalf of someone else (`createdonbehalfby` set) is rejected as `impersonated`.
- The proof task must be unmodified since creation: `createdon == modifiedon`, `modifiedby == createdby`, `modifiedonbehalfby` empty; a missing field counts as modified. Otherwise `record_modified`.
- The subject must match exactly one task. Zero or more than one matching task returns no proof (`no_proof`), so a second task with the same subject cannot confuse the lookup.
- Binding the code to the browser: the nonce is visible in the CRM for a moment, so possession of it is not enough. `secret` never enters the CRM; the service stores only its SHA-256 hash and compares it on `/verify` (constant time). A wrong `secret` does not burn the nonce, so a third party cannot lock out the victim.
- The user needs permission to create tasks in the CRM; otherwise sign-in is refused with a clear message.

## Service-account read policy
`ServiceReadPolicy` allows one narrow exception for the proof lookup: only `tasks`, subject `NLQ-login-<43 characters>`, a fixed field list (creator, created on, modified on, modified by, modified on behalf by), `top` at most 2. Every other read of `tasks` through the service path is refused before any request is sent.

## Token
- HMAC-signed with `Auth__SigningKey` (at least 256 bit, supplied as an environment variable or secret).
- Claims: account (`DOMAIN\user`), CRM user id, groups, expiry (`Auth:TokenHours`, default 8, no sliding window), token version. Revocation happens at expiry; rotating the key revokes all tokens.
- `BearerAuthentication` builds the `ClaimsPrincipal`; groups are read from the token, so `IsInRole` works.
- Rate limit: 30 requests per minute per address on the auth endpoints (`AuthRateLimiter`).

## Groups
At sign-in the user's CRM security roles and teams are read with a narrow service read (`systemusers(<id>)/systemuserroles_association` and `.../teammembership_association`, `$select=name`, `$top` up to 200). They are written into the token as `role:<Name>` and `team:<Name>`; the prefix prevents a team named like a role from impersonating it. Names are sanitized (no control characters, at most 100 characters, at most 100 groups, no duplicates). If the service account may not read them, groups are empty (least privilege) and sign-in continues. The group-to-role mapping uses these names (see [Group mapping and App.Use](group-mapping-and-app-use.md)).

## Hand-off to other tabs
The result page and the admin panel are separate pages without the widget's in-memory token.
- A signed-in widget calls `POST /api/auth/handoff` and receives a code valid for 30 seconds. The code is a different token purpose: it is not accepted as a session, and a session token is not accepted as a code. The code does not outlive the parent session.
- The widget opens the service page with `window.open` and posts the code with `postMessage` only to its own opened tab, and only after that tab announces itself (`nlq-ready`) from the service origin.
- The page accepts the code only from `window.opener` and only from an origin returned by `GET /api/auth/origins`; it then calls `POST /api/auth/redeem`, receives a session token, and keeps it in that tab's `sessionStorage`. The code is never placed in an address, which prevents login CSRF through a crafted link.
- The panel adds the bearer header to its API calls and downloads files with an authenticated fetch.

## Settings
| Key | Meaning |
|---|---|
| `Identity:Mode` | `CrmProof`, `Windows`, or `Fixed` (development only; refused in production unless `Identity:AllowFixedInProduction`) |
| `Auth:TokenHours` | Session token lifetime, default 8 |
| `Auth:ChallengeSeconds` | Nonce lifetime, default 120 |
| `Auth__SigningKey` | Token signing key (environment variable) |

## Endpoints
`POST /api/auth/challenge`, `POST /api/auth/verify`, `POST /api/auth/handoff`, `POST /api/auth/redeem`, `GET /api/auth/origins`. The first two and the hand-off pair are intentionally public (they establish identity) and rate limited.

## Security and privacy
- Audit records for sign-in hold the user and time only, never the nonce, secret or token.
- A CRM that cannot be reached cannot sign users in (queries would fail anyway).
- XSS in the CRM page hosting the widget could expose a session; tokens are short-lived and held only in memory or per-tab storage.

## Out of scope
LDAP group lookup; ADFS/OAuth authentication of the service account to the CRM (a separate provider concern).
