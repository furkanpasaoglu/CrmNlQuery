# Chat Sessions

Status: Implemented.

## Summary
The widget works like a chat application: a new chat per topic, a list of earlier chats on the left, rename, pin, delete, delete-all. Everything is private to the signed-in user. Result rows are never stored; only the question, the query, the entity and chat metadata are.

## Goals
- Continue an earlier conversation where it stopped.
- Keep follow-up context per chat; one chat's query is never the context of another.
- Bound storage growth with a retention period that is visible to users.

## Concepts and rules
- A **chat** is an owner's ordered list of question/answer records (`history` rows) plus a title and a pinned flag.
- A chat is created by its first answer. "New chat" only opens an empty draft in the widget; nothing is created on the server until the first question is asked.
- **Follow-up context** is loaded on the server from the chat's last answer (question, entity, FetchXML; no rows), within the `Conversation:ContextMinutes` window.
- **Pinned** chats are kept regardless of retention and listed in their own group at the top.
- **Title:** first 40 characters of the first question; renamable up to 60 characters; sanitized on the server and rendered as text only.
- Old messages store no rows. Opening an old message's table re-runs the query with the user's current CRM rights.

## Data model
- `conversations(id, owner, title, pinned, created_at, updated_at)`, indexed on `(owner, pinned desc, updated_at desc)`.
- `history.conversation_id`, indexed on `(conversation_id, created_at)`.
- `user_preferences(owner, display_name)`.

## API
All endpoints require a signed-in caller and touch only the caller's own chats; another user's or a missing chat is a 404.

| Endpoint | Purpose |
|---|---|
| `GET /api/chats?take&cursor&q&pinned` | Keyset-paged list (`updated_at,id`); `take` clamped to 1-100; `q` searches titles on the server. Also returns `retentionDays`. |
| `PATCH /api/chats/{id}` | `{ title?, pinned? }` |
| `DELETE /api/chats/{id}` | Delete the chat and its records |
| `DELETE /api/chats?includePinned=false` | Delete all history; pinned chats are kept by default; returns the deleted count |
| `GET /api/chats/{id}/messages?take&cursor` | Messages, paged newest-first, returned oldest-to-newest within a page |
| `GET /api/chats/counts` | Counts used by the delete-all confirmation |
| `POST /api/ask` | Accepts `chatId`; creates a chat when absent and returns its `chatId` |
| `GET/PUT /api/me/preferences` | `{ displayName }` (30 characters, sanitized) |

Write methods from the widget's origin are allowed by the CORS policy and `CrossSiteGuard`; writes from any other origin are rejected.

## Widget behavior
- Opening the widget (and every page load) shows a new empty draft; the last chat is not opened automatically. The empty state greets the user by display name and offers example questions.
- Left list: Pinned, Today, Yesterday, Last 7 days, Older; server-side search; per-row menu (rename, pin/unpin, delete). Loaded 30 at a time, more on scroll, with skeleton rows while loading.
- Opening a chat loads the latest 20 messages and scrolls to the bottom; scrolling up loads earlier groups of 20 without moving the reading position. Only the last answer's table is shown immediately; earlier messages show their question and entity, and the table is re-run with "Show".
- Every load failure shows a short message with a retry action.
- Window states: minimize, full screen, close, resize from the corner; state is remembered in `localStorage` (every access guarded; defaults when unavailable).
- Delete confirmations are in-widget dialogs (never `confirm/alert`, which would block the CRM page). Delete-all shows server-supplied counts and an unchecked "also delete pinned chats" option. The dialog notes that only chat history is removed, not CRM data.

## Retention and privacy
- `History:RetentionDays` applies at chat level: unpinned records past the period are deleted, and a chat with no records left is deleted. Pinned chats are untouched. `0` disables deletion. The setting can be changed at runtime.
- The widget always shows the rule ("Chats are deleted after N days (pinned chats are kept)"), using the `retentionDays` value returned by `GET /api/chats`.
- Question text is never written to the admin audit log, application log or metrics. Administrators cannot read other users' chats.
- Chat titles and display names are user input: sanitized on the server, written as text only, and never sent to the model.

## Out of scope
Sharing or exporting chats, in-chat search, storing result rows, giving the model earlier rows.
