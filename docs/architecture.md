# Architecture

CrmNlQuery follows Clean Architecture: business rules sit in the center and every dependency points inward. The rules below are enforced by tests in `tests/CrmNlQuery.Tests/Architecture`, so a violation fails the build rather than a code review.

## Layers and dependency direction

```
Web ───────────► Composition ──► Infrastructure
 │                    │                │
 └──► Application ◄───┴────────────────┘
          │
        Domain
```

`Eval` (the evaluation runner) sits beside `Web`: it depends on Application, Domain and Composition only.

| Project | Holds | May depend on |
|---|---|---|
| `Domain` | Business concepts only: entities, enums, constants, static rules (access, ask steps, metadata, glossary, history, knowledge, quality...). No packages, no technology names. | nothing |
| `Application` | Use cases and the ports they need, grouped by feature (Ask, Access, DataPolicy, Glossary, Knowledge, Quality, Observability, Operations, Setup...). Only package: the logging abstractions. | Domain |
| `Infrastructure` | Adapters: `Persistence/{Postgres,Memory}/<area>` and `External/{Crm,Llm}`. | Application, Domain |
| `Composition` | The only place that wires ports to adapters (`AppComposition`, `EvalComposition`, `PipelineFactory`, `SetupComposition`, configuration sources). | Application, Domain, Infrastructure |
| `Web` | HTTP only: `Endpoints/{Admin,Ask,Auth,Setup}`, `Contracts` (DTOs), `Security`, `Identity`, `Hosting`, static `wwwroot`. Cannot see Infrastructure. | Application, Domain, Composition |
| `Eval` | Evaluation runner: `Models`, `Services`, `Suites`. | Application, Domain, Composition |

Inside Infrastructure, each technology folder (`Persistence.Postgres`, `Persistence.Memory`, `External.Crm`, `External.Llm`) may not use another; they meet only through Application ports. Every feature has both a PostgreSQL and an in-memory store, verified by shared contract tests.

## Feature folders

Application features use the same shape:

- `Abstractions`: every interface and delegate (ports), including port contract records.
- `Models`: records, enums and catalogs.
- `Services`: behavior.
- `Exceptions`: feature-specific exceptions.

Large features add specialist subfolders, for example `Ask/Planning{,/Prompts,/Validation}`, `Ask/Steps`, `Quality/{Runs,Comparison}`, `Observability/{Metrics,AccessLog,Calls}`, `Operations/{Settings,Startup}`. Inside Ask the direction is fixed: Services and Models, then Steps, then Planning, then Abstractions; ports know nothing of the layers above them.

Domain types are grouped by area (`Domain/<Area>`) and category (`Entities`, `Enums`, `Constants`, `Services`, `Exceptions`). Tests mirror the source tree.

## File and folder rules

- One top-level type per file, and the file name equals the type name.
- Folder structure equals namespace structure.
- Every Application interface lives in an `Abstractions` folder.
- Web `*Dto` types live under `Contracts`; `*Endpoints` classes under `Endpoints`.
- A folder that grows past roughly ten files is split into subfolders by responsibility.

## What the tests enforce

| Test class | Protects |
|---|---|
| `ArchitectureTests` | project and compiled-assembly references point only inward; every project belongs to a layer; Domain has no packages, Application only logging abstractions; Infrastructure technology folders never use each other; public types live under their assembly namespace |
| `CleanArchitectureLayoutTests` | one type per file named like the file; ports in `Abstractions`; Domain category folders; Domain free of technology names and `System.Net`, `System.Data`, `Microsoft.*` usings; DTO and endpoint placement |
| `ApplicationInternalsTests` | the Ask-internal direction (Abstractions, Planning, Steps, use case) |
| `FolderStructureTests` | namespace equals folder; no source folder hidden by ignore rules |
| `IdentityRulesTests` | CRM data paths require an explicit `Caller`; the evaluation caller cannot be enabled by the service composition |

## Main request flow

1. `POST /api/ask` reaches the Web endpoint, which identifies the caller (`RequireCaller`), applies the request gate and deadline (`AskGuard`), and resolves language.
2. `AskPipeline` (Application/Ask) loads schema through `IMetadataSource`, which in the service is wrapped by the data access policy.
3. The question analyzer (model) picks the target entity and mentions; record names are resolved in the CRM as the caller; ambiguity produces a clarifying question.
4. If the question needs several steps, the decomposer splits it; steps run in order and later steps receive earlier results as ids filled in by code, never by the model.
5. The plan generator (model) produces FetchXML from the filtered schema, resolved records and optional knowledge and glossary context.
6. `PlanValidator` checks the FetchXML against the schema and limits; failures are fed back to the model for a bounded number of retries.
7. The query executor runs the validated query against the CRM, read-only and impersonating the caller (`MSCRMCallerID`); the access log decorator records the read.
8. The result table builder turns rows into a table (with labels, an explanation, optional chart and suggestions) and the outcome is returned as a DTO and stored in the caller's history. Result rows are never sent to the model.
