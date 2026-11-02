# Deployment guide (Docker)

CrmNlQuery runs as two containers: the service and PostgreSQL (with pgvector). Users keep working in Dynamics 365 as usual; the chat window appears inside CRM pages and acts as the signed-in CRM user.

## 1. Requirements

- **Docker host:** Docker Engine with Compose (Linux, or Windows with WSL2). A local model (Ollama or vLLM) needs an NVIDIA GPU and the NVIDIA Container Toolkit.
- **Network:** the Docker host can reach the CRM over HTTP(S); users' browsers can reach port 5080 of the Docker host (or a TLS reverse proxy in front of it). When the CRM uses `https`, the service must be served over `https` too, because browsers block mixed content. Terminate TLS in a reverse proxy.
- **CRM:** Dynamics 365 on-premises with the Web API enabled (9.0; 8.2 is the target version), and a service account (see below).
- **Addresses:** the CRM address, and the address users open the service on (the widget loads from it).

## 2. Service account in the CRM (once, by a CRM administrator)

The service account is **not** an administrator. It is a least-privileged CRM user (for example `DOMAIN\svc-nlquery`, Access Mode = Non-interactive) that holds a dedicated security role. The scripts in `tools/` create and verify the role:

```powershell
.\tools\Setup-ServiceAccount-Crm.ps1 -ReadTables role,team,account,contact,opportunity,product,contract,contractdetail,incident,salesorder
.\tools\Check-ServiceAccount.ps1
```

- `role,team` let the service read a user's security role and team names at sign-in; without them groups stay empty and sign-in still works.
- The table list is the set of tables questions can be asked about. A query that touches a table outside the list is rejected. Add custom tables or `activitypointer` there, then open them in the admin panel under *Data access*.
- Optional features need extra privileges: `-OpenInCrm` (personal views) and `-CreateTables account,contact` (record creation).

The CRM applies the effective privilege as the **intersection** of the service account and the impersonated user, so the service account must hold read privileges for every table users may ask about, and each user is still limited by their own roles.

## 3. What users need in the CRM

Sign-in works through a short-lived task: the user's browser creates a small task in the CRM with the user's own session and deletes it; the service reads who created it. Therefore:

- Users must be able to create a task and delete their own task (default roles allow this).
- Being signed in to the CRM is enough. No extra password, AD or Kerberos setup is needed; the CRM may use Windows, AD FS or any other sign-in method.
- The task contains only a one-time code and no personal data, and it is deleted within seconds.

## 4. First-run setup wizard (recommended)

Leave `CRM_PASSWORD` empty in `.env` and the service starts in **setup mode**:

1. `cp .env.example .env`, set `POSTGRES_PASSWORD` to a long random value.
2. `docker compose up -d --build`
3. `docker compose logs app` prints a line like `FIRST-RUN SETUP ... code: XXXX-XXXX-XXXX`. The code is single use and changes every start.
4. Open `http://<service address>:5080/setup` and enter the code (three wrong tries lock it for five minutes).
5. **CRM:** address, domain, service account, password, then *Test connection*. The wizard checks sign-in, schema access, user lookup, reading on behalf of another user, role and team access, the sign-in task, and warns if the account holds the System Administrator role.
6. **Model:** Ollama, vLLM or Anthropic, address, model name, key, then *Test connection*. A model server that is not on the same machine requires a network confirmation; the cloud model requires an explicit confirmation because question text leaves the company.
7. **Administrator and address:** the first administrator (`DOMAIN\user`) and the address users open the service on, then *Finish setup*.
8. The service restarts itself (`restart: unless-stopped`) and the page opens the tool when it is ready.

Where settings live: secrets (CRM password, signing key, API keys) are stored encrypted with AES-GCM in the `app_config` table; the key is `/data/config.key` on the `appdata` volume and readable only by its owner. Back up the database **and** the key file together. Values saved by the wizard take precedence over `.env` and environment variables.

To run the wizard again: `docker compose run --rm app --reset-setup`, then start the service.

### Manual configuration (optional)

When `CRM_PASSWORD` is set the wizard does not start and everything is read from `.env`:

| Variable | Meaning |
|---|---|
| `CRM_BASE_URL`, `CRM_DOMAIN`, `CRM_USERNAME`, `CRM_PASSWORD` | The service account used to reach the CRM |
| `IDENTITY_MODE=CrmProof` | Users prove who they are through the CRM (recommended) |
| `AUTH_SIGNING_KEY` | Session signing key, at least 32 random characters. Keep it secret; changing it signs everyone out |
| `DEV_USER` | First administrator account (`DOMAIN\user`) |
| `BIND_ADDRESS` | `127.0.0.1` (this machine only) or `0.0.0.0` (the network) |
| `POSTGRES_PASSWORD` | Password of the bundled PostgreSQL |
| `LLM_PROVIDER`, `OLLAMA_*`, `VLLM_*` | Model settings: `Ollama` (default), `Vllm`, or `Anthropic` for non-sensitive data only |

A model server is accepted only on this machine or on hosts listed in `TrustedHosts`; any other remote address is refused.

## 5. After startup

- Open the admin panel from the chat window: *Settings > Help > Open the admin panel* (signed in as the first administrator).
- In the panel, open the tables you want questions to cover under *Data access* and give users roles.
- The service does not install a vLLM server: set `VLLM_MODEL` and `VLLM_API_KEY` in `.env` and run `docker compose --profile vllm up -d`.

## 6. Adding the chat window to the CRM

On the CRM web server (administrator PowerShell, IIS URL Rewrite module required):

```powershell
.\tools\Install-ChatWidget.ps1 -ServiceUrl https://<service address>
```

Use `-Remove` to uninstall.

## 7. Verification

- [ ] Open a CRM form (hard refresh): the chat button appears.
- [ ] Ask "Show accounts in <city>": the first question signs in automatically and an answer arrives.
- [ ] The admin panel opens; *My identity* shows your account and your `role:...` / `team:...` groups.
- [ ] With another user, only records that user is allowed to see are returned.
- [ ] No lingering task whose subject starts with `NLQ-login-` remains in the CRM.

## 8. Security notes

- If the CRM address is `http`, the service logs a warning: Windows credentials travel unencrypted. Use `https` outside a test environment.
- Keep the service account least-privileged; never use an administrator account.
- The service only talks to models inside your network. The cloud model (`Llm:Provider=Anthropic`) requires `Llm:AllowCloud=true` and should only be used with non-sensitive data.
- A session token lives only in the browser tab that holds it and is valid for eight hours.
- HSTS and TLS belong to the reverse proxy. If the CRM sets its own Content-Security-Policy, the widget may need `connect-src` / `script-src` entries for the service address.

## 9. Operational limits

- Run a single service instance. Quota and concurrency counters are held in memory; running several instances needs a shared store.
- Groups are CRM security role and team names (`role:Sales Manager`, `team:Support`), not AD groups.
- The service reaches the CRM with Windows (NTLM) authentication. AD FS-only CRM endpoints are not supported yet.
- Knowledge-base embeddings use Ollama (`bge-m3`); a vLLM-only installation has no embedding provider yet.
- After setup, changing connection settings means re-running the wizard (`--reset-setup`).
