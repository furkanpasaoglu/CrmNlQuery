# Tools

PowerShell and Node scripts for setting up a CRM, seeding test data and running evaluations. Scripts that write to a CRM or run evaluations against it refuse to run unless `$env:NLQUERY_LAB_CRM = '1'` is set, so they cannot be pointed at a production system by accident. None of them stores a password: they prompt for it and keep it in memory.

## CRM and deployment

| Script | Purpose |
|---|---|
| `Setup-ServiceAccount-Crm.ps1` | Creates the minimal security role for the service account (read access to the listed tables, impersonation, role and team lookup) and assigns it |
| `Check-ServiceAccount.ps1` | Read-only check of that role and its privileges |
| `Probe-ServiceImpersonation.ps1` | Read-only proof that the service account can read as another user and that the user's own limits still apply |
| `Setup-ServiceAccount-AD.ps1` | Creates the service account in Active Directory and denies interactive logon on the app server |
| `Install-ChatWidget.ps1` | Injects the chat window into the CRM's IIS site with an outbound URL Rewrite rule (`-Remove` undoes it) |

## Test data

| Script | Purpose |
|---|---|
| `Seed-EvalData.ps1` + `lab-data.json` | Adds fictional accounts, contacts, opportunities, cases and more to a test CRM, idempotently |
| `Create-CustomTables.ps1` + `custom-schema.json`, `custom-data.json` (`Build-CustomData.js` regenerates the data) | Creates a small set of custom tables with fictional data |
| `Dump-EvalFacts.ps1` | Prints facts from a test CRM (read-only), used when writing evaluation cases |

## Evaluation

| Script | Purpose |
|---|---|
| `Run-Regression.ps1` | Unit tests and every evaluation pack against a test CRM |
| `Run-LocalModel.ps1` | The evaluation packs one after another against a local model, with per-case timing |
| `Run-LoadTest.ps1` | N simultaneous virtual users against the local model; reports latency per user count against the targets |

## Repository checks

| Script | Purpose |
|---|---|
| `Scan-UiText.js` | Counts hard-coded user-facing text and fails when it grows past `ui-text-baseline.json` (`--check`, `--list`, `--write-baseline`) |
