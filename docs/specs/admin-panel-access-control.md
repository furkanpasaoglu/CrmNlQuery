# Admin Panel Shell and CRM-Independent Access Control

Status: Implemented.

## Summary
The admin panel is an extensible shell (left menu, user box at the bottom, theme) bound to the tool's own permission system instead of a CRM role. A future page (documents, glossary) arrives by adding one menu entry and one permission.

Not covered here: AD group to role mapping, the contents of the documents and glossary pages, password or LDAP sign-in (identity comes from Windows; there is no password), and chat usage permission (`App.Use` exists as a structure only).

## Identity
- Identity is the Windows account (`DOMAIN\user`) authenticated by the host, `WindowsAccount.From`. A fixed identity mode exists for development only.
- Account comparison is case-insensitive and culture-independent (`NameNormalizer.Fold`); the folded form is the storage key.
- There is no sign-out: the browser signs in with Windows automatically. The user menu explains this.

## Data model (PostgreSQL; the in-memory store follows the same contract tests)
| Table | Columns |
|---|---|
| `admin_users` | `account` (folded, PK), `display_name`, `active`, `created_at`, `created_by` |
| `admin_roles` | `id` (uuid), `name` (unique), `builtin`, `created_at` |
| `admin_role_permissions` | `role_id`, `permission` (a constant defined in code) |
| `admin_user_roles` | `account`, `role_id` |
| `admin_audit` | `id`, `at`, `actor`, `action`, `target`, `detail`; append-only |
| `app_logs` | see Application logs |

## Permissions
The list is fixed in code, not in the database. Initial set: `Dashboard.View`, `Logs.View`, `Users.Manage`, `Roles.Manage`, `Audit.View`, `App.Use`. A new feature adds a new permission constant; the Roles page reads the list from code. The built-in role **Administrator** holds every permission (including future ones) and cannot be edited or deleted; other roles are created by administrators.

## Rules
1. **Fail closed:** no identity, unregistered user, inactive user, or an unreachable store means no permission. A store failure is 503, never "allow".
2. **Existence hiding:** admin endpoints return 404 to unauthorized users; an unreachable store returns 503.
3. **No escalation:** a user cannot add a permission they lack to a role, nor grant a role that contains a permission they lack.
4. **Last Administrator is protected:** the last active Administrator cannot be deleted, deactivated, or have the role removed.
5. The Administrator role cannot be edited or deleted, even by a holder of `Roles.Manage`.
6. **Bootstrap:** `Admin:Bootstrap` is a list of accounts. If the store has no active Administrator, those accounts are added as Administrator at startup (audit entry `bootstrap`). If an Administrator exists the setting is ignored. If it is empty and no Administrator exists, nobody can open the panel (a warning is logged).
7. **Audit:** adding, deleting or deactivating users and roles, role-permission changes, role assignment and removal, and bootstrap are written to `admin_audit` (who, what, whom). Question text and CRM data are never written.
8. Permission lookups use a 60-second cache that is cleared immediately when an administrator changes something.
9. A user who was deactivated cannot be deleted unless they never performed any action (the audit trail is preserved).
10. Users are added by typing `DOMAIN\user`; there is no directory search.

## API (all under `/api/admin/...`, cross-site protected, no CORS)
| Endpoint | Permission |
|---|---|
| `GET /me` (name, account, roles, permissions, menu permissions) | any registered user |
| `GET /report`, `GET /health` | `Dashboard.View` |
| `GET /logs` (level, text, time range) | `Logs.View` |
| `GET/POST/PUT/DELETE /users...` | `Users.Manage` |
| `GET/POST/PUT/DELETE /roles...`, `GET /permissions` | `Roles.Manage` |
| `GET /audit` | `Audit.View` |

`/me` returns 404 to unregistered users. The UI draws the menu from the `/me` permissions, but authorization is always enforced on the server. `GET /roles` is also readable with `Users.Manage` (an endpoint can list "any of" several permissions).

## Application logs
Serilog also writes to PostgreSQL: `app_logs(id, at, level, message, exception, request_id)`, level Warning and above by default (`Logging:Database:MinimumLevel`). Writing uses a bounded queue and batches; a database failure never breaks a request. Question text and CRM rows never enter the log. Retention follows `Metrics:RetentionDays`. The rolling file log remains as a fallback.

## UI
- `admin.html` hosts the shell: menu, user box, theme, hash routing; each page is a menu entry with title, icon, permission and renderer.
- Theme: light / dark / system plus five accent colors, stored in `localStorage` (default used if unavailable).
- Accessibility: keyboard navigation, `aria-current`, `aria-expanded`; status is shown with icon and text, not color alone.
- A page opened without permission shows "no access" in the UI and 404 from the server.

## Settings
`Admin:Bootstrap` (initial administrator accounts), `Metrics:RetentionDays`, `Logging:Database:MinimumLevel`.
