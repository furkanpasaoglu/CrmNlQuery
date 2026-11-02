# Security Policy

CrmNlQuery sits between users, a CRM and a language model, so security reports are taken seriously.

## Project status

This is a personal open-source project maintained by one person. It has **not had an independent security audit**, and it has been verified against a Dynamics 365 on-premises v9.0 trial installation only. Please evaluate it in a test environment before putting it near production data. Treat the design as a set of defensive measures, not as a guarantee.

## Supported versions

Only the latest commit on the default branch receives fixes. There are no long-term support branches.

## Reporting a vulnerability

Please report privately and do not open a public issue with details.

- Use GitHub's private reporting: open the repository's **Security** tab and choose **Report a vulnerability**.
- If that option is not available, open a public issue that says only that you have a security report (no details, no exploit) and ask for a private channel.

Helpful details to include:

- What is affected (component, endpoint, configuration) and the impact you see.
- Steps to reproduce, ideally against your own test environment.
- The commit or version you tested.
- Whether the issue needs a signed-in user, an administrator, or no authentication.

This is a best-effort, volunteer-run process. You can expect an acknowledgement and a fix or a decision as time allows, with no guaranteed response time. Credit is given in the fix notes if you want it.

## In scope

- Authentication and identity: the CRM-proof sign-in, session handling, impersonation, any way to act as another user or as the service account.
- Authorization: roles and permissions in the admin panel, the table and field allow-list, per-user result and history isolation.
- Query safety: any way to make the service run a write, read a table or field that is closed, or return data the caller could not read in the CRM.
- Model-related attacks: prompt injection through user questions, knowledge-base documents or CRM data that changes what is executed or exposed.
- Web issues: cross-site scripting, request forgery, header and CSP problems, injection.
- Secrets handling: the encrypted settings store, the setup wizard, logs that expose credentials or row data.

## Out of scope

- Findings that require an already compromised host, database or CRM administrator account.
- Denial of service through sheer request volume (quotas exist, but the service is designed to run as a single instance behind your own network controls).
- Vulnerabilities in third-party software (Dynamics 365, Ollama, vLLM, PostgreSQL, Docker). Please report those upstream.
- Missing hardening that is documented as the operator's responsibility, such as TLS termination, HSTS and a reverse proxy.
- Social engineering or physical attacks.

## Known limitations

These are documented and are not treated as new reports: see [Security notes](docs/deployment.md#8-security-notes) and [Operational limits](docs/deployment.md#9-operational-limits) in the deployment guide. Examples: run a single service instance, use `https` outside a test environment, and keep the service account least-privileged.

## Testing guidelines

Please test only against an environment you own or have written permission to test. Do not access data that is not yours, and do not run load or destructive tests against systems you do not control. Do not include real customer data in reports.

## For operators

- Keep the CRM service account least-privileged and never use an administrator account.
- Use `https` for the CRM and put the service behind a TLS-terminating reverse proxy.
- Review the data-access allow-list in the admin panel before opening the chat to users.
- Leave write features (such as record creation) off until you have reviewed them.
- Keep the setup code and the `.env` file private.
