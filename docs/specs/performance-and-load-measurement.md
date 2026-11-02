# Performance and Load Measurement

Status: Implemented.

## Summary
Answers "how many people can ask at once, and how long do they wait" for a local model with numbers, and provides a way to measure the effect of disabling thinking on mechanical steps. Decisions about a model server, quantization or a smaller model are taken from these measurements; nothing is changed without measuring.

## Load-test mode (eval)
- `--users 1,3,5` (with `--cases <file>`): for each level, N virtual users ask simultaneously, each sequentially (closed loop: a user asks the next question after receiving the answer). The job list is the same at every level (cases times `--rounds`, default 1) and is drawn from a shared queue, so levels are comparable.
- Per level the output reports total time, job count, questions per minute, the latency users experience (p50, p95, slowest), passed/failed cases, total output tokens and tokens per second.
- A warm-up case runs first and is discarded, so model loading does not distort the figures.
- The real pipeline is used (analysis, decomposer, plan, CRM execution, validation). Result rows and question text are not written to files; only case id, duration and pass/fail.
- Output goes to the console and to `eval/results/load-<model>-<time>.json`.
- Wrapper: `tools/Run-LoadTest.ps1` (prompts for the CRM password, checks the model server, records the environment settings in the result).

## Capacity targets
The load test compares two p95 values with targets and writes MET or MISSED to the console and the result JSON:
- simple questions: p95 at or below `--target-simple` (default 30 s);
- multi-step questions (conditional, decomposed, chained; cases marked `"multiStep": true`): p95 at or below `--target-multi` (default 60 s).

`Run-LoadTest.ps1` exposes `-TargetSimple`, `-TargetMulti` and `-Rounds`. Acceptance is both targets MET at the user counts of interest (for example 3 and 5 concurrent users). With few cases, p95 approaches the slowest value, so repeat with `-Rounds 2` before deciding. The targets are defaults, to be adjusted per deployment.

## Thinking-off experiment (settings only)
Thinking can be disabled per step through `Ollama:ThinkAnalyze`, `Ollama:ThinkDecompose` and `Ollama:ThinkPlan`. The procedure runs the speed suite (`eval/speed-cases.json`) through `tools/Run-LocalModel.ps1 -Only speed -Label <name>` in three configurations: baseline, analysis and decomposer thinking off, and all three off (set the settings as environment variables such as `Ollama__ThinkAnalyze=false`, and remove them afterwards). Decision criteria: accuracy does not fall below baseline (speed suite plus the main case and conditional suites), per-case duration drops, and the number of cases above the answer time limit decreases. If disabling thinking in the plan step raises errors, a possible follow-up is retrying with thinking on after a failed no-thinking attempt.

## Security and privacy
Evaluation runs against test data only; row data never reaches the model (the pipeline is unchanged); the CRM password is read from the environment or prompted and is never written to a file.

## Out of scope
Server provider comparison (`vllm-provider.md`), quantization and smaller-model comparison, and a capacity table are separate follow-ups that use these measurements.
