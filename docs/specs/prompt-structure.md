# Prompt Structure and Size Reduction

Status: Implemented.

## Summary
Model prompts are shortened and made prefix-cache-friendly without lowering accuracy. This keeps a local model fast and prevents a large schema from overflowing the context window (`prompt-size-and-context-window.md`).

## Where prompt size comes from
- **Analyzer** (`LlmQuestionAnalyzer`): the entity list (logical name | label) dominates the message. It is identical on every question and costs roughly 10-16 tokens per entity, so a schema with hundreds of entities puts thousands of tokens into the list alone.
- **Planner** (`LlmPlanGenerator`): almost all of the user message is the attribute, option and relationship blocks of the selected entities. Wide tables such as account or contact are very large, and most of their relationships and technical attributes are irrelevant to the question.
- **Decomposer** (`LlmQuestionDecomposer`): fixed rules and examples.
- When a column question is asked and then answered, the same analysis would otherwise run twice.

## Design

### Analysis cache
`AnalysisCache` (Planning) keeps the analysis result for a short time (`Planning:AnalysisCacheMinutes`; eval `--analysis-cache on`). The key is user + question + entity list + knowledge text + previous query + open-record id; any change misses. Only the analysis structure (entity, record and column names) is stored, never row data, and the key is per user so one user's input never serves another. Eval disables it so repeated runs really call the model. A column-question turn needs one analysis call instead of two.

### Cache-friendly ordering ("stable first, variable last")
- Analyzer: `system` = rules + sorted entity list (+ follow-up and open-record rules after the list); `user` = knowledge, previous question, open record, then the question.
- Planner: `user` = sorted entity blocks, then knowledge, previous query, step info, resolved records, and `Question`/`Columns` last; on retry, previous errors after that.
- With the same entity set the prefix stays identical, so the server's prefix cache avoids re-evaluating it.

### Context pruning
`PlanContextPruner` (`Planning:PruneContext`, default on; eval `--prune off`, `Run-Regression.ps1 -NoPrune`) removes irrelevant detail from the planner's entity blocks with rule-based filters:
- Relationships are listed only toward entities in the request (primary plus related, or a resolved record's entity); other relationships are dropped.
- Always kept: primary id and name, all lookup and relationship fields, `statecode`/`statuscode`, requested columns, anything whose name or label appears in the question or knowledge text, option-set fields.
- Dropped: tracking/system fields (`versionnumber`, `importsequencenumber`, `timezoneruleversionnumber`, `utcconversiontimezonecode`, `overriddencreatedon`), `*_base` money bases, `yomi*` fields, virtual `...name` helper columns when the base attribute exists, `address2_*`/`address3_*` unless the question asks for them, and types the planner cannot use.
- Safety net: the first attempt uses the pruned context; on a validation error such as an unknown attribute or relationship, the retry receives the full unpruned list.

### Option labels
`MetadataProvider` loads option labels for multi-select picklists (`MultiSelectPicklist`) and for Yes/No (`True`/`False` options) in addition to picklist/state/status. The cache format is versioned. `PlanValidator` accepts `contain-values` / `not-contain-values` for multi-select fields and `true`/`false` for Boolean fields; if a multi-select query fails on an older CRM the feature is skipped.

### Many-to-many relationships
`MetadataParser`/`MetadataProvider` read `ManyToManyRelationships` (`ManyToManyInfo`: intersect table and the attributes on each side; `EntityInfo.ManyToMany`). The planner prompt lists only N:N relationships whose both ends are in the request (or in a resolved record), once, with a note on joining through the intersect table. `PlanExamples` includes an intersect-table example, and the plan cache fingerprint includes it.

### Neutral examples and hints
Planner examples are generated from the request's own entities (`PlanExamples`) instead of fixed entities the CRM may not have; the system message carries no examples, and a "Root entity" line is added. The "which kind of record" hint comes from metadata (`UserMessages.WhichKindOfRecord`). Widget example questions come from `Widget:ExampleQuestions` (at most 5).

### Null ordering rule
CRM places NULL first in an ascending sort, so "the 3 with the fewest employees" would return nulls. The plan prompt requires adding a not-null condition on any field used for smallest/fewest/earliest ordering.

### Per-step thinking and keep-alive
`Ollama:ThinkAnalyze`, `Ollama:ThinkDecompose`, `Ollama:ThinkPlan` (each falls back to the general `Ollama:Think` when empty; eval `--think` overrides all) and `Ollama:KeepAlive` (for example `"30m"`, seconds, or `-1` for never unload). Defaults send nothing, so behavior is unchanged unless set.

## Not done by design
- No semantic answer cache: cached answers could cross users with different rights.
- Entity-list narrowing in the analyzer is not implemented; it risks recall loss and would be added only if measurement showed the list does not fit, validated with the full suites.
- A different model server is a separate provider (`vllm-provider.md`).

## Measuring the effect
Prompt size per step is read from `eval --dump-prompts true` (it writes each prompt and the model's reply), the per-call `sizes` in eval result JSON, and the "Model calls by step" table (`prompt-size-and-context-window.md`). A change is accepted only if every suite (cases, chains, context, conditional/golden, decomposer, knowledge, one-sentence, Turkish names, custom tables) is no worse. Comparison is by estimated tokens per step (average and largest) and by model time. Pruning is accepted when a repeated run of a flaky multi-step case is not worse with pruning than without.
