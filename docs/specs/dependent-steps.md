# Dependent Multi-Step Plans

Status: Implemented.

## Summary

Some questions depend on the result of an earlier one: "The contacts of the company with the most open opportunities." The model plans all steps up front; code runs them in order and passes the previous step's record ids into the next one. The model never sees ids or rows and never re-plans from intermediate results (there is no agent loop).

This extends [conditional-plans.md](conditional-plans.md) with value passing between steps.

## Scope

- At most 3 sequential steps. A step may depend on the ids of an earlier step; independent steps may share a plan and run sequentially.
- The conditions `Always`, `IfPreviousEmpty`, and `IfPreviousNotEmpty` continue to work.
- At most 100 ids pass between steps; more asks the user to narrow the question.
- An empty previous step stops the chain with an explanatory message.
- Every step runs on behalf of the calling user (impersonation); a record the user cannot see never reaches a later step.
- The answer shows the final step's table plus a short summary of earlier steps. GUIDs are never shown.

Anti-goals: the model seeing row data or ids, silently picking one record on a tie, ids leaking into a field that points at a different entity.

## Placeholder

A dependent step's FetchXML uses a fixed token `{{stepN}}` (N is the earlier step's number) and only in this form:

```xml
<condition attribute="X" operator="in"><value>{{step1}}</value></condition>
```

A single `eq` is also allowed. `StepInputFiller` replaces the token with the real GUIDs (one `<value>` each), re-validates, and runs the step.

Validator rules for placeholders:

- Only earlier steps may be referenced; forward or self references are errors.
- Only on id or lookup attributes, and only with `eq` or `in`.
- Type compatibility: the earlier step's entity must match the entity the target attribute points to (or the entity's own primary id). For multi-target attributes such as `Customer` or `Owner`, the target is resolved from metadata and rejected when ambiguous.
- A step flagged as dependent that uses no placeholder, and a placeholder in a step not flagged as dependent, are both errors.
- An invented GUID that is not a known record is rejected separately; a placeholder is not a GUID.

## Where ids come from

A step's output id is a column aliased `_rowid`.

- Plain query: the root entity's primary id, added by code.
- Aggregate query: grouping is by an id or lookup attribute, not by name: `<attribute name="customerid" groupby="true" alias="_rowid" />`. The validator checks that a step whose ids are needed does this.
- Results carry `ResultTable.RowIds`. More than 100 stops the chain with a "narrow the question" message.

## Ranking and ties

When the producing step is an aggregate with ordering and `top N`, `TieHandling` widens `top` (up to 100) and keeps every row whose measure equals the Nth row's value. If ties exceed N, the answer notes it (for example "2 companies tied; both were used"). If the producing step has no `top`, or one at or above the row cap, N is taken as 1. The model only writes a natural `top N`.

## Aggregate limit

When the CRM's aggregate row limit is exceeded, the user gets a fixed message to narrow by date, status, or city.

## Decomposer trigger

The decomposer runs when `ConditionalQuestionFilter.MayNeedSteps` matches a deliberately broad word list ("most", "least", "highest", "then", Turkish "en çok", "olanın", and so on), matched after `NameNormalizer.Fold` normalization (case, Turkish i/ı, suffixes). Under-triggering yields a silently wrong answer, while over-triggering only costs latency, so the list errs wide.

Safety net: the single-step planner may return a `needsSteps` flag in the plan schema; when set, the question falls to the decomposer. The flag is ignored inside a chain.

## Answer

`AnswerOutcome.StepNotes` continues; `StepSummaries` adds, per earlier step, number, name or question, row count, up to 5 preview rows, column names, and the selected record names, but no ids. The widget and result page show a short block above the table, for example "Step 1: company with the most open opportunities -> Alpine Pharma Distribution (7)". This is data read under the caller's permissions and is not sent to the model or logged.

## Clarifying questions

As in conditional plans: a step that returns a question stops the flow; after the answer the flow reruns from the start; the question key carries the step number.

## Design summary

- The decomposer emits `QuestionStep`s with `UsesStep` (int?); at most 3 steps, each with `question`, `condition`, `usesStep`.
- The planner prompt for a dependent step names the entity the previous step produced and says to use its ids only through `{{stepN}}` with `in`.
- `AskPipeline.RunChainAsync` runs a step, takes its `_rowid` ids (at most 100), fills the next step's placeholder, re-validates, and runs it.
- The plan cache key is the placeholder form; filling happens after the cache.
- The stored query is the final FetchXML with ids filled in, valid on its own. The chain is not re-run; the result page runs this stored query and notes that it depends on the earlier step's records at the time. It is visible only to its owner.

## Chain answers

For chain answers, "Open in CRM" and narrowing hints are disabled. Follow-up questions attach to the last step of the chain, using the stored final query as the previous query (see [follow-up-questions-and-form-context.md](follow-up-questions-and-form-context.md)).

## Security and privacy

- The model writes placeholders only; real ids are inserted by code after validation.
- Each step runs under the caller's identity.
- Step summaries contain data the caller may read and are not logged or sent to a model.

## Out of scope

Agent loops in which the model reads intermediate results and re-plans, parallel execution, more than 3 steps, and carrying row values (only ids) between steps.
