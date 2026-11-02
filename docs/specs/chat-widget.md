# Chat Widget

Status: Implemented.

## Summary
A floating button and chat panel injected into CRM pages. Analysts ask questions without leaving the CRM; the widget calls the service's `POST /api/ask` and renders the answer. The widget is a single script, `wwwroot/widget/widget.js`, served by the service.

## Goals
- Ask a question from any CRM page and see the answer as a table, with clarification questions, step notes and the query explanation.
- Never break the host CRM page (no CSS or JS collisions).
- Never write CRM-supplied values as HTML.
- Sign the user in without a separate login prompt (see [CRM proof sign-in](crm-proof-sign-in.md)).

## Behavior
- A round button sits at the bottom right; clicking opens the panel. The panel has a chat list on the left, the conversation in the middle and the question box at the bottom (see [Chat sessions](chat-sessions.md) and [Stop button](stop-button.md)).
- The answer table supports search and paging. Clarification questions are answered inline. Step notes and the "understood as" explanation are shown under the answer.
- When the CRM page is a record form, the widget reads the entity name and record id from the page address (classic and Unified Interface forms) and sends it as the form context, so "this account" can be resolved.
- All user-facing text lives in one catalog per language (`T_EN`, `T_TR`); the language is the user's preference.
- Only the top-level window hosts the widget; if the CRM re-renders its DOM, the button is put back.

## Design
- **Service address.** Taken from the widget script's own address (`<script src=".../widget/widget.js">`); the widget calls the same origin. Nothing is hard-coded and no file is copied to the CRM server.
- **Isolation.** The UI lives in a Shadow DOM, so CRM styles do not affect the widget and the widget does not affect the CRM.
- **Rendering.** CRM-derived text is written with `textContent` only. Pure logic blocks (address parsing, day grouping, token freshness, and so on) are delimited with `// BEGIN x` / `// END x` markers so they can be unit-tested in Node.
- **Placement.** The recommended placement is a CRM web resource (JavaScript) that loads `widget.js` from the service. An IIS outbound-rewrite rule (`tools/Install-ChatWidget.ps1`, with a `-ServiceUrl` parameter and `-Remove` to undo) is the fallback. Both are configured with the service address and the CRM origin.
- **CORS.** The service allows only the configured origins (`Cors:AllowedOrigins`, defaulting to the CRM origin), and only on the endpoints the widget needs. Write methods from other origins are rejected by `CrossSiteGuard`.
- **Authentication.** The widget calls the API with `Authorization: Bearer <token>`. On a 401 it runs the CRM proof flow and retries; the token is kept in `sessionStorage` (that tab only). If `/api/auth/challenge` returns 404 (the service is not in CrmProof mode) the widget does nothing.

## Security and privacy
- The service never trusts the widget for identity; every call is authenticated by the bearer token.
- A CRM with a strict Content-Security-Policy needs `script-src` / `connect-src` allowances for the service address.

## Out of scope
A separate desktop client.
