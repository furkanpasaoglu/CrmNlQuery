# Group Mapping and App.Use Gate

Status: Implemented; off by default.

## Summary
Directory (or CRM role/team) groups can be mapped to roles, and an optional gate can require the `App.Use` permission for the user-facing endpoints. The default behavior is unchanged: the gate is off, and everything that depends on groups happens only when an administrator turns it on. Errors resolve to "no permission", never to a crash or lock-out. The service adds nothing to the customer's directory; administrators map their own existing groups to roles.

## The `Access:RequireAppUse` setting
A three-valued runtime setting (`SettingKind.Choice`, panel group "Access"). Default `off`.

| Value | Behavior |
|---|---|
| `off` | No check or query is performed. |
| `report` | Nobody is blocked. For a user without `App.Use`, an audit entry `access.would_deny` is written with the account, endpoint, reason and the group names from the user's token, plus one log line. |
| `on` | Without `App.Use` the request gets 403 with a plain message and an `access.denied` audit entry (same detail). If the permission store cannot be reached the answer is 503 and access is never granted. In `report` mode a store failure lets the request through with a Warning. |

- Applied to all user endpoints (`RequireCaller`): questions, results, chats, preferences. The admin panel is not affected, so an administrator cannot lock themselves out (the built-in Administrator role holds every permission, and the bootstrap account signs in without any group).
- `AppUseGate` (Application/Access) takes the account, a membership query, the mode, the endpoint and a group describer, and returns Allow, Deny(reason) or Unavailable. The describer runs only when a record is actually written, subject to the 10-minute limit of `RecordAccessAsync`, so the hot path pays nothing. Detail format: `needs App.Use; <reason>; groups: A, B, C` (at most 40 groups, 800 characters). Untranslatable SIDs appear as SID text.

## Group-to-role mapping
Group names come from the token (`TokenGroups`): CRM roles and teams as `role:<Name>` / `team:<Name>` in CRM-proof mode, Windows token groups in Windows mode, and `Identity:FixedGroups` in development. Membership is evaluated with `IsInRole` against the mapped names, case-insensitively. A user who is not registered but belongs to a mapped group is admitted; an explicitly inactive user row wins. Role-granting rules are the same as for individual users.

Mapping a group to the Administrator role, or mapping a broad group (Domain Users, Everyone, Authenticated Users, Users and similar) to a role carrying management permissions, requires explicit confirmation on the server (`409` with `code: confirm_required`, accepted with `confirm=true`; only newly added roles are evaluated). A confirmed grant is audited as "CONFIRMED administrator grant". The panel shows a confirmation card.

## Diagnostics
- **My identity** page (`GET /api/admin/identity`, `RequireAdmin(null)`): the signed-in account, identity mode, group names in the token, the mapped groups the user belongs to, resolved roles and permissions, whether `App.Use` is held, and the current `Access:RequireAppUse` mode. Only the caller's own identity is shown.
- **Pick from my groups** on the AD groups page lists only the signed-in administrator's own groups, with the exact names the token carries. Typing a group name by hand remains possible.

## Security and privacy
- When the setting is `off`, no additional work runs.
- A failed membership lookup or name translation means "not a member" (or the SID text), never extra privilege.
- Audit and log records contain group names only: no passwords, token contents, or identifiers other than the account and SIDs.

## Settings and permissions
| Item | Notes |
|---|---|
| `Access:RequireAppUse` | `off` (default), `report`, `on` |
| `App.Use` | Permission required by the gate; unaffected by the panel |

## Out of scope
Creating directory groups or accounts, a default role or seed mapping, nested-group or LDAP lookups, and a "domain users" SID rule.
