# Evaluation

The evaluation runner (`src/CrmNlQuery.Eval`) asks the questions in these packs through the real pipeline and checks the answers.

```bash
dotnet run --project src/CrmNlQuery.Eval -- --cases eval/cases.json --model <model> [--case c01-all-products] [--repeat 3]
```

| File | What it checks |
|---|---|
| `cases.json` | Core single-question cases |
| `chain-cases.json`, `context-chain-cases.json` | Follow-up chains, with and without form context |
| `context-cases.json` | Form context ("this account's contacts") |
| `multistep-cases.json` | Dependent multi-step questions and their single-step traps (also used by `--decompose-cases`) |
| `knowledge-cases.json`, `knowledge-realistic-cases.json`, `knowledge/` | Company knowledge (retrieval) with short and realistic documents |
| `glossary-cases.json`, `glossary-entries.json` | Business glossary terms |
| `turkish-name-cases.json`, `oneshot-cases.json` | Turkish suffixes on names and one-sentence versions of chains |
| `custom-cases.json` | Custom tables |
| `summary-cases.json`, `draft-cases.json`, `report-cases.json`, `create-cases.json` | Customer summary, e-mail drafts, report finder, record creation |
| `date-group-cases.json`, `chart-intent-cases.json` | Date grouping and chart intent |
| `closed-field-cases.json`, `closed-field-stored-cases.json`, `policy-lab*.json` | Data access policy: closed tables and fields must never be used |
| `design-cases.json` | Customization design assistant |
| `speed-cases.json` | A small set used for latency measurements |
| `data-fingerprints.json` | Fingerprints of the reference data, to tell a data change from a regression |

A case can carry a fixed expectation (`rowCount`, `mustContain`) or a **reference query** (`expect.reference`): the runner executes both the model's query and the reference query on the live data and compares the result sets, so the cases keep working when the test data changes. See `docs/specs/reference-query-evaluation.md`.

`eval/generated/` (git-ignored) receives acceptance sets generated from a schema (`--generate-set`, see `docs/specs/starter-acceptance-set.md`). Questions are in Turkish and English on purpose.
