# CrmNlQuery

**Ask your Dynamics 365 on-premises CRM questions in plain language. Your data never leaves your network.**

[Türkçe](README.tr.md)

CrmNlQuery adds a chat window to Dynamics 365 (on-premises) pages. A business analyst types "show accounts in Istanbul with open opportunities over 100k", and a local language model turns it into a validated, read-only FetchXML query that runs **as that user**, so the CRM's own security still decides what they can see. The result appears as a table, a chart or a summary.

> Screenshots and a short demo are in [`docs/images`](docs/images) *(added after the first public release)*.

## Why it exists

Many companies still run Dynamics 365 on-premises, often because their data may not leave the building. Cloud copilots are not an option for them, and report building stays with a few specialists. CrmNlQuery brings natural-language access to those installations:

- **Local models.** Ollama or vLLM on your own hardware. A cloud model is possible but disabled unless you explicitly allow it, and it is meant for non-sensitive data only.
- **The model never sees row data.** It only sees the question and the schema. Records are read by code, as the signed-in user.
- **Human in the loop.** Anything that writes to the CRM is previewed and needs an explicit click; the service account is read-only by design.
- **Auditable.** Every read is logged (who, which tables, how many rows) without storing the data itself.

## Highlights

- Natural language to FetchXML for Turkish and English questions
- Multi-step questions: conditions ("if there are none, ...") and dependent steps ("the contacts of the account with the most open opportunities")
- Follow-up questions, form context ("this account's contacts"), explanations, next-step suggestions
- Charts, customer summaries, e-mail drafts, report finder, business glossary, knowledge base (RAG)
- Chat window inside CRM pages with sessions, stop button, slash commands and feedback
- React administration panel: users and roles, data access policy, audit trail, quality checks, settings, export
- Least-privilege security model: CRM-proof sign-in, impersonation, allow-listed reads, quotas, no inline scripts
- Docker deployment with a first-run setup wizard; secrets are stored encrypted
- An evaluation framework that checks answers against reference queries on live data

The full list is in [`docs/features.md`](docs/features.md).

## How it works

```
 CRM page                        CrmNlQuery service                           Dynamics 365
┌──────────────┐   question    ┌─────────────────────────────────────┐
│ chat widget  │ ────────────► │ analyze ─► resolve names ─► plan    │
│ (shadow DOM) │               │   (model)     (CRM read)    (model) │
│              │               │              │                      │
│              │   table/chart │       validate (code, allow-lists)  │  FetchXML, as the user
│              │ ◄──────────── │              ▼                      │ ───────────────────────►
└──────────────┘               │     execute ─► build table / chart  │ ◄───────────────────────
                               └─────────────────────────────────────┘      rows (never to the model)
```

The service is a .NET 10 application organised as Domain / Application / Infrastructure / Web layers (dependency rules are enforced by tests, see [`docs/architecture.md`](docs/architecture.md)). PostgreSQL with pgvector stores history, settings, the audit trail and the knowledge base.

## Quick start

Requirements: Docker with Compose, a Dynamics 365 on-premises organization (Web API enabled), a least-privileged CRM service account, and Ollama (or another supported model server).

```bash
cp .env.example .env          # set POSTGRES_PASSWORD to a long random value; leave CRM_PASSWORD empty
docker compose up -d --build
docker compose logs app       # prints the one-time setup code
```

Open `http://localhost:5080/setup`, enter the code and follow the wizard: it tests the CRM connection and the model before anything is saved. Then add the chat window to the CRM with `tools/Install-ChatWidget.ps1`. The full guide, including the CRM service account, is in [`docs/deployment.md`](docs/deployment.md).

## Repository layout

| Path | Content |
|---|---|
| `src/` | Service: `Domain`, `Application`, `Infrastructure`, `Composition`, `Web` (HTTP and the chat widget), `Eval` (evaluation runner) |
| `admin-ui/` | Administration panel (React, Vite, Mantine) |
| `tests/` | Unit tests (xUnit), architecture tests, widget and catalog checks (Node) |
| `eval/` | Evaluation packs: questions with expected results |
| `tools/` | Setup and evaluation scripts (service account, widget install, regression, load test) |
| `docs/` | Design specs, architecture, deployment guide, feature list |

## Development

```bash
dotnet test --filter "Category!=Live"            # unit tests; "Live" tests need a CRM or PostgreSQL
for f in tests/widget/*.test.js; do node "$f"; done
cd admin-ui && npm ci && npm run build           # admin panel, written to src/CrmNlQuery.Web/wwwroot/admin-next
```

The project is developed spec-first: each feature starts as a short design document in `docs/specs`, then a test, then code. Hard-coded user-facing text is not allowed; every message lives in a catalog with English and Turkish side by side, and a test enforces it.

## Status

- Developed and verified against Dynamics 365 (on-premises) 9.0. Version 8.2 is the compatibility target.
- Record creation from chat is implemented but off by default; it needs an extra privilege on the service account.
- Windows (NTLM) authentication is supported from the service to the CRM; AD FS-only endpoints are not supported yet.
- Run a single service instance. See the limits in [`docs/deployment.md`](docs/deployment.md#9-operational-limits).

## License

[MIT](LICENSE)
