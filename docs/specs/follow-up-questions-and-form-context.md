# Follow-Up Questions and Form Context

Status: Implemented.

## Summary

Analysts rarely finish in one sentence; they narrow and change direction step by step: "active customers in Istanbul", then "of these, those with a quote but no order", then "show them with lost-opportunity reasons". A follow-up question builds on the previous result's query, keeping its filters, and can also move to a related entity ("the invoices of these companies"). Form context lets a user standing on a CRM record ask "this customer's contacts".

## Goals

- Follow-ups of two kinds: (a) add a filter or ordering on the same entity, (b) move to a related entity.
- Automatic context across a conversation in the widget; the result page's "narrow" action uses the same mechanism.
- Visibility: the answer says "Builds on your previous result: <previous question>", offers "Ask as a new question", and the widget header has "Start over".

## Non-goals

- Passing previous result rows or row ids to the model or into the query.
- Hand-written calculations such as ratios or period comparisons.

## Core rules

1. **Context is the previous query, not rows.** The model receives the previous question, entity, and validated FetchXML. The new query rewrites the previous filters, using `link-entity` when moving to a related entity. No rows, id lists, or counts are carried, so the rule "row data never reaches the model" holds, no URL-length limits apply, and the query always runs fresh under the caller's CRM permissions.
2. **No chain history.** Accumulation lives in the FetchXML itself; context is only the last answer. Growth is bounded by the plan limits (at most 4 link-entities, depth 3, 25 conditions); beyond them the plan is rejected with "This chain got too complex; ask it as a new question."
3. **Follow-up or new question?** The question analyzer returns `referencesPrevious` (bool). References such as "of these", "these companies", "now from this list" are true. With no previous context, or when unclear, it is false (a new question). The analyzer sees only the previous question and entity name; the planner sees the FetchXML.
4. **The server loads context.** The client sends only `previousResultId`; the server reads the history record bound to the owner. A missing, foreign, or expired record silently counts as a new question, without revealing that it exists. Context lasts at most `Conversation:ContextMinutes` (default 30, range 5-240) after the last answer.
5. **Same entity:** columns stay as in the previous result and the column question is not repeated. If the entity changes, the normal column question is asked.
6. **Name resolution** only resolves proper names in the new sentence; earlier ones already sit in the FetchXML as ids.
7. **Knowledge search** for follow-ups uses previous question plus new question.
8. **Text hygiene and limits** are unchanged: previous question text enters the prompt as data; previous FetchXML is collapsed to one line.
9. **Result page "narrow"** is the same mechanism. `POST /api/result/{id}/refine` is kept and runs a follow-up internally.
10. **Gate and metering are unchanged:** follow-ups go through `AskGuard` and `MeteredAsk`; metering records only a "follow-up" flag.

Conditional and dependent multi-step questions use the previous context only as described in [dependent-steps.md](dependent-steps.md); conditional questions do not.

## User experience

- The widget keeps the last result id in memory and `sessionStorage` and sends it as `previousResultId`.
- A follow-up answer shows the "Builds on your previous result" note and an **Ask as a new question** button that resends the same question without context (`forceNew`). "Start over" in the header clears context.
- Clarifying questions (record choice, columns) are answered with the same context.
- The result page offers "Continue from this result" (`ForceContinue`).

## Design

- Domain: `PreviousQuery(Question, Entity, FetchXml)`; `QuestionAnalysis.ReferencesPrevious`.
- Application/Ask: `AskRequest.Previous`; `LlmQuestionAnalyzer` returns `referencesPrevious`; the plan prompt gains a "Previous query" section only in a follow-up (otherwise the prompt is unchanged): keep its filters unless the user changes them; to move to a related entity make it the root and reproduce the filters with `link-entity`; never use ids. A narrowing fragment is a filter value, not a record name, and name fragments use `like %...%`.
- Application/History: `ConversationContext` loads the owner's previous answer within the time window; `RefineService` uses it.
- Web: `/api/ask` accepts `previousResultId` and `forceNew`; `OutcomeDto.basedOn` carries the "builds on" note.
- Persistence: `history.parent_id` links a result to its predecessor; `requests.follow_up` records the flag. The dashboard shows a "Follow-up questions" share.
- Setting: `Conversation:ContextMinutes`.

## Plan rules that support chains

- "Does not have" conditions use an outer join with a null test on the root filter (`entityname` set), not a filter inside the join.
- Name fragments use `like %...%`, not `eq`.
- `MentionText.StripTurkishSuffixes` removes known Turkish suffixes after an apostrophe from a mention (for example "Acme'nin" becomes "Acme"); names such as "Levi's" and "O'Brien" are untouched.

## Form context

A user with a record form open can ask "this customer's contacts".

- The widget reads `etn` and `id` from the page address (classic and Unified Interface forms, `%7b..%7d` braces) and sends `context {entity, id}` with the question.
- `RecordContextResolver` validates the GUID and entity and reads the record under the caller's permissions. If unreadable, the context is silently ignored.
- The analyzer returns `referencesCurrentRecord` (unused when unsure). The record reaches the planner as a resolved record (GUID); the answer carries `AboutRecord` ("About: <name>"), and the widget shows a dismissible "Open record" tag.
- Without context, the analysis and plan prompts are unchanged. A reference such as "this firm" with no context does not run a query with an invented GUID (`UnknownGuidError`).

## Security and privacy

- Only query text, never rows, reaches the model.
- Context lookups are bound to the owner; there is no cross-user leakage.
- Form context reads use the caller's permissions, so a record the user cannot read adds nothing.

## Settings

`Conversation:ContextMinutes` (5-240, default 30).

## Out of scope

Resolving people or teams ("Ahmet's team"), calculations over results, and actions such as creating follow-up tasks.
