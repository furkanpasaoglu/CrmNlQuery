# Prompt Size and Context Window (num_ctx)

Status: Implemented.

## Summary
Pass the context window explicitly to the local model server (Ollama), turn silent truncation of an oversized prompt into an explicit error, and measure the size and duration of every model call. The measurements are the baseline for prompt optimization (`prompt-structure.md`).

## Why
Without an explicit window, Ollama uses its own default and silently drops the start of a prompt that does not fit: rules or schema vanish and the model still returns a plausible but wrong answer. Without per-call size metering, "which step's prompt grew" cannot be answered.

## Behavior
1. **`Ollama:NumCtx`** (integer, default 16384) is sent as `options.num_ctx` on every request. It is a file/environment setting, not a panel setting, because changing it reloads the model. `0` or empty omits the option (old behavior; a startup warning is logged).
2. **Overflow detection.** When a window is configured the request also carries `"truncate": false`. Ollama then answers an oversized prompt with HTTP 400 `exceed_context_size_error` and the real counts (`n_prompt_tokens`, `n_ctx`); `OllamaClient` maps this to `PromptTooLongException` with cause `prompt`. Relying on `prompt_eval_count` alone is not sufficient, because a silently truncated prompt reports the truncated length.
   - As a fallback, `done_reason == "length"` or prompt plus output tokens at or above the window minus 8 gives cause `generation` (thinking plus output filled the window; the server's `eval_count` includes thinking tokens). This check is best-effort.
   - No pre-flight check: a character-to-token estimate is unreliable for Turkish, JSON and GUID-heavy text. A warning is logged when the estimate reaches 80% of the window.
   - Older Ollama versions may ignore `truncate`; the required server version belongs in the installation notes.
   - The Anthropic client has no such check.
3. **User message.** A fixed message (`UserMessages.ContextOverflow`): the question needs more model context than configured; ask a shorter question or ask an administrator to raise the context size. The outcome kind is `context_overflow`, mapped like a model error but with its own message. Internal details (step, token counts, window) go to the log only. Metering records `error_type` `context_overflow_prompt` or `context_overflow_generation`.
4. **Metering.** `MeteredLlmClient` records for each call `prompt_chars` (system + user + schema), `est_tokens` (= chars / 3.5, rounded; a rough estimate, hence the prefix), real input/output tokens and duration. `ai_calls` has nullable `prompt_chars` and `est_tokens` columns. One information log line per call (logger `CrmNlQuery.ModelCalls`): step, provider/model, chars, estimated and real tokens, duration, result. No question or prompt text is logged. The admin panel's "AI calls by step" table shows the average prompt size.
5. **Eval output.** The eval runner wraps the model client in a recorder (`LlmCallLog`/`RecordingLlmClient`) with an in-memory collector. The result JSON has per-case call counts, total `prompt_chars`, input/output tokens and model time (`sizes`), and the summary prints per-step averages ("Model calls by step") and a "would NOT fit" list against the configured window. A cloud run has no `num_ctx`, so this list is how oversized cases are found with a cloud model; the definitive answer comes from a local-model run.

## Notes
- `prompt_eval_count` is the total prompt length including tokens served from the prefix cache. The cache effect shows in `prompt_eval_duration`: a cold prompt of about 12k tokens takes seconds, a warm repeat with the same prefix takes milliseconds, and several prefixes can stay warm at once. This is why prompts are ordered "stable first, variable last".
- The default 16384 is chosen so a 27B-class model plus its KV cache fits a 24 GB GPU; larger windows may not. When a schema needs more, the failure is visible and the metering shows which step caused it.
- Not part of this feature: field pruning, a strict schema, automatic window selection, and any semantic answer cache (a cached answer could cross users with different CRM rights, so none exists).

## Settings
`Ollama:NumCtx` (default 16384; `0` disables). Metering has no switch.
