# Design specs

The project is developed spec-first: each feature starts as a short design document here, then a test, then code. The system architecture is described in [`../architecture.md`](../architecture.md).

## Asking questions

- [Natural-Language Query](natural-language-query.md)
- [Conditional Multi-Step Plans](conditional-plans.md)
- [Dependent Multi-Step Plans](dependent-steps.md)
- [Follow-Up Questions and Form Context](follow-up-questions-and-form-context.md)
- [Duplicate Row Prevention](duplicate-row-prevention.md)
- [Total Count and Narrowing Suggestions](total-count-and-narrowing.md)
- [Schema Change Detection](schema-change-detection.md)
- [Query Explanation](query-explanation.md)
- [Follow-up Suggestions](follow-up-suggestions.md)
- [Charts](charts.md)

## Beyond plain queries

- [Customer Summary](customer-summary.md)
- [Email Draft](email-draft.md)
- [Report Finder](report-finder.md)
- [Business Glossary](business-glossary.md)
- [Knowledge Base (Retrieval-Augmented Context)](knowledge-base.md)
- [Open in CRM](open-in-crm.md)
- [Record Creation from Chat](record-creation.md)
- [Customization Design Assistant](customization-design-assistant.md)

## Chat window

- [Chat Widget](chat-widget.md)
- [Chat Sessions](chat-sessions.md)
- [Stop Button](stop-button.md)
- [Slash Commands](slash-commands.md)
- [Feedback (Thumbs Up/Down) and Golden-Set Candidates](feedback.md)
- [Multilingual UI (Turkish / English)](multilingual-ui.md)

## Identity, access and audit

- [CRM Proof Sign-In](crm-proof-sign-in.md)
- [Caller Identity Enforcement](caller-identity-enforcement.md)
- [Group Mapping and App.Use Gate](group-mapping-and-app-use.md)
- [Admin Panel Shell and CRM-Independent Access Control](admin-panel-access-control.md)
- [Data Access Policy (Table and Field Allow-List)](data-access-policy.md)
- [Data Access Log and Question Text Viewing](data-access-log.md)
- [Denied-Action Audit](denied-action-audit.md)
- [Guardrails, Limits and Runtime Settings](guardrails-and-limits.md)

## Administration

- [Administrator Export](admin-export.md)
- [First-Run Setup Wizard](setup-wizard.md)

## Models and prompts

- [vLLM Provider](vllm-provider.md)
- [Prompt Structure and Size Reduction](prompt-structure.md)
- [Prompt Size and Context Window (num_ctx)](prompt-size-and-context-window.md)

## Quality and evaluation

- [Quality Checks](quality-checks.md)
- [Scheduled Quality Runs](scheduled-quality-runs.md)
- [Starter Acceptance Set from the Schema](starter-acceptance-set.md)
- [Reference-Query Evaluation Mode](reference-query-evaluation.md)
- [Performance and Load Measurement](performance-and-load-measurement.md)
