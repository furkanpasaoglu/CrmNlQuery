# Stop Button

Status: Implemented.

## Summary
A question can take minutes with a local model. The user can stop it: the widget aborts the request, and the server stops model generation, CRM queries and later chain steps. No half result or chat trace remains.

## Goals
1. The UI frees immediately and the question text returns to the input box.
2. The server stops model calls, CRM queries and remaining steps of a multi-step chain.
3. Nothing is saved for a stopped question (no chat, no result).
4. The stop remains observable on the server.

## Mechanism
The widget cancels `fetch` with an `AbortController`. The server already carries ASP.NET's `RequestAborted` token through the whole question pipeline (model calls, CRM queries, chain steps, the overall deadline). No new endpoint and no server-side cancellation state are needed.

The overall deadline (`Limits:AskDeadlineSeconds`) and a user cancellation are kept apart: an expired deadline stays a `timeout`; only a client disconnect is `cancelled`. `RequestContext.WatchDeadline` / `DeadlineExpired` let `AskGuard` report which one occurred.

## Widget behavior
- While a question is running the Send button becomes "Stop" and is always clickable.
- On Stop: the request is aborted, the "thinking" bubble and the user's bubble for that question are removed, the text goes back into the input box with focus, and a short "Stopped" notice is shown.
- During a clarification round (the user answered an option or column question), only the "thinking" bubble is removed; the earlier question and the clarification stay so the choice can be repeated.
- A stopped question never becomes the "previous question" for follow-up context, and a response that arrives after the abort is not rendered.

## Server behavior
- An aborted request cancels the running model call and CRM query; the step in progress is cut and later chain steps never start.
- A stopped question creates no chat and stores no result (`ChatTurnService.CompleteAsync` writes nothing on cancellation). If the abort arrives after the save has begun, the record is either completed or not written at all.
- The quota is not refunded, so start-and-stop cannot bypass it. The concurrency slot is always released.

## Audit and metrics
- **Data access log:** queries completed before the stop stay recorded. A CRM query cut mid-flight gets one row with outcome `cancelled` and zero rows (also for a query cut by the deadline). No question text, FetchXML, filter values or record ids are written.
- **Requests metric:** exactly one `requests` row per stopped question: `outcome = cancelled`, `error_type` = the stage at the time (`analyze`, `decompose`, `plan`, `crm`, `resolve`; in chains `step2:plan`, `step2:crm`, ...; `unknown` otherwise), duration, owner, time. `RequestContext.Stage` is updated by the metered LLM client and the access-logging wrappers.
- **Dashboard:** `cancelled` is excluded from the error rate and shown as its own count.

## Out of scope
Stop for result-page refinement or result re-runs; automatically cancelling when the user switches chats (the answer is ignored and the server finishes); a separate cancel endpoint.

## Notes
A reverse proxy that forwards client disconnects late delays cancellation; a dedicated cancel endpoint would be the remedy.
