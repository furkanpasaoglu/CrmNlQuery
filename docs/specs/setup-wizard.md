# First-Run Setup Wizard

Status: Implemented.

## Summary
After `docker compose up -d`, the operator opens `/setup` in a browser and completes five steps; the service is then ready. No `.env` file has to be filled in by hand. The wizard tests the CRM connection, the service account's permissions and the model server while setting up, and states exactly what is missing.

## When setup mode runs
Setup is needed when `Setup:Completed` is not `true` and no CRM password is configured (`SetupComposition.Needed`). In that case the service starts a small separate host (`SetupHost`) that serves only `/setup`, its static files and `/api/setup/state|unlock|test-crm|test-model|complete`; there is no fallback route. Installations that already provide their configuration through environment variables or files never enter setup mode.

After completion the service stops itself and the container's restart policy (`restart: unless-stopped`) starts it in normal mode; the page tells the operator to refresh shortly. Starting with `--reset-setup` deletes the stored setup settings and exits, so the wizard runs again on the next start. Settings cannot be reset from the panel.

## Security
- **One-time setup code:** generated at startup in setup mode and written only to the service console log, so only someone with access to the server sees it. It is held in memory as a digest. Three wrong attempts lock the unlock endpoint for 5 minutes. A successful unlock yields a session valid for 30 minutes, sent in the `X-Setup-Session` header.
- Passwords and keys are write-only: stored, never returned by any endpoint, never logged or audited.
- Setup endpoints require same-origin requests (`CrossSiteGuard`); there is no CORS.
- Test calls go only to the address the operator entered. The scheme must be http or https, non-local addresses produce a warning, redirects are not followed, the timeout is 10 seconds, and response bodies are never returned (only a result summary).
- The data-locality protection for the model address is preserved: a non-local model address needs an explicit confirmation that the address is on the operator's own network, which is then written to `TrustedHosts`. A cloud model requires an explicit acknowledgement that only lab or fictitious data will be used.

## Steps
1. **Welcome and code:** enter the setup code.
2. **CRM connection:** address, domain, user, password. A checklist shows pass/fail with a hint for each missing item (`CrmSetupProbe`): the CRM answers and WhoAmI works (warning for plain `http`); Web API version; the service account can impersonate users (`prvActOnBehalfOfAnotherUser`) and read users and schema; role and team reading (if missing, groups stay empty, with the `Setup-ServiceAccount-Crm.ps1 -ReadTables role,team` hint); a warning if the account is an administrator.
3. **Model:** provider (Ollama, vLLM, or cloud for lab use), address and model name (`LlmSetupProbe`). "Test" checks that the server is reachable and the model exists. vLLM's maximum model length is not asked (it is a server option).
4. **Addresses:** the service address that users see (the widget is loaded from it) and the CRM origin (for CORS, suggested from the CRM address). The result is a ready-to-copy `Install-ChatWidget.ps1 -ServiceUrl ...` command to run on the CRM server.
5. **First administrator and finish:** the Windows account (`DOMAIN\user`) that may open the admin panel, and a summary. On "Save": settings are written, the session signing key is generated automatically, the identity mode is set to CRM proof (`CrmProof`), setup is marked completed, and the service restarts itself.

## Settings storage
- Table `app_config(key, value, secret, updated_at)` in PostgreSQL. Secrets (CRM password, signing key, cloud key) are encrypted with AES-GCM (`AesGcmConfigCipher`). The encryption key is a random 256-bit key in `/data/config.key` on the data volume, created on first start with restrictive permissions (`ConfigKeyFile`). This keeps a database backup or dump from exposing secrets; it does not protect against someone with access to both the volume and the database, which operators should know.
- Configuration order, later sources overriding earlier ones: `appsettings.json`, environment variables, stored settings (`app_config`), command line. Stored settings therefore override environment variables, because Compose always passes default values for many variables.
- If the volume is lost, the encrypted secrets cannot be decrypted; the service reports this clearly and asks to run setup again with `--reset-setup`.

## Design
- Application/Setup: `SetupService` (state, code verification and lockout, step probes, completion), `SetupInputValidator`, `AppConfigMapper`, `AesGcmConfigCipher`, `ConfigKeyFile`; ports `IAppConfigStore`, `IConfigCipher`, `ICrmProbe`, `IModelProbe`.
- Infrastructure: PostgreSQL `app_config` store and the configuration source loaded at startup; probes `CrmSetupProbe` and `LlmSetupProbe` that use the real clients with temporary options and save nothing.
- Web: `Endpoints/Setup/SetupEndpoints`, `wwwroot/setup/` (plain HTML and JS, Turkish/English catalogue, CSP-compliant), `Hosting/SetupHost`.
- `docker-compose.yml`: `restart: unless-stopped`, a `/data` volume, and no mandatory CRM password variable.

## Out of scope
ADFS connection, TLS setup, changing settings from the panel after setup, downloading models into Ollama, languages other than Turkish and English, multi-instance operation.
