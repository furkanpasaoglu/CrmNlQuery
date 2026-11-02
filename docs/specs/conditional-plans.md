# Conditional Multi-Step Plans

Status: Implemented.

## Summary

Some questions do not fit one query: "Show the contracts of A and B, or those of C if there are none." The service splits the question into at most three sequential steps and runs each through the normal pipeline. Code, not the model, decides whether a later step runs, based only on the previous step's row count.

## Goals

- Sequential plans of at most 3 steps.
- Two conditions only: run if the previous step returned 0 rows (`IfPreviousEmpty`), or at least 1 row (`IfPreviousNotEmpty`). The first step always runs (`Always`).
- The answer states which step ran or was skipped, and why.
- Single-step questions behave exactly as before.

## Non-goals

Parallel steps, an agent loop, carrying values between steps (see [dependent-steps.md](dependent-steps.md)), result aggregation, and a router.

## Behavior rules

- "Empty" means the previous step returned 0 rows (the record exists but has no matching data).
- If a company cannot be found at all, that step fails and the plan stops. A typo cannot be told apart from a real absence, so the plan never falls through to the "else" branch.
- If a step returns a clarifying question, the flow stops and the question is returned. After the answer the flow runs again from the start (the server is stateless). In multi-step plans the question key carries the step number (for example `s2:resolve:<name>`); single-step keys are unchanged.
- The table shows only the last step that ran, with a note above it. Steps that came back empty do not get their own table.
- The model never sees row data; the condition looks at the row count only.

## Design

Types:

- `PlanStep(string Question, StepCondition Condition)` with `StepCondition { Always, IfPreviousEmpty, IfPreviousNotEmpty }`.
- `IQuestionDecomposer.DecomposeAsync(question)` returns 1-3 `PlanStep`s. The model-based `LlmQuestionDecomposer` uses schema-constrained JSON. Invalid output (4+ steps, a condition on the first step, an empty question) is retried once, then the original question falls back to a single step.
- `AnswerOutcome.StepNotes` (`IReadOnlyList<string>`, empty by default) is an optional field in the `POST /api/ask` response, so existing clients keep working.

Flow in `AskPipeline`:

1. The question goes to the decomposer.
2. A one-step result runs the normal path unchanged.
3. A multi-step result evaluates each step's condition in order and runs eligible steps through the normal pipeline. A failure stops the flow with a "Step N: ..." message; a question stops the flow and is returned; an answer records its row count and adds a note.
4. Skipped steps are noted. The response carries the last step that ran, with all notes.
5. If every step is empty, the last executed step's empty table is returned with the notes.

Unchanged: every step's FetchXML passes `PlanValidator`, execution is read-only, results are capped at 500 rows, and no row data reaches the model.

The web page shows `StepNotes` above the table.

## Trigger

The decomposer runs only when the question contains a conditional marker (for example "if none", "yoksa"), so ordinary questions pay no extra model call.

## Risk

A decomposer that splits a single "and" question (for example "A and B's contracts") into two steps would create a wrong condition. Evaluation cases for multi-company `in` queries guard against this.

## Out of scope

Parallel execution, an agent loop, value passing between steps, aggregation, and a router.
