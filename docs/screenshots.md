# Screenshots

All screenshots use fictional data and a local model. Questions run as the signed-in CRM user.

## Chat window inside Dynamics 365

**A plain question: the answer is a table, with the plain-language explanation above it and next-step suggestions below.**

![A table answer](images/chat-table-answer.jpg)

**Aggregate questions become charts, with a table view and a PNG download.**

![A chart answer](images/chat-chart.jpg)

**Customer summary: built by code from fixed queries; the model never sees the records.**

![Customer summary, top](images/chat-customer-summary-top.jpg)
![Customer summary, bottom](images/chat-customer-summary-bottom.jpg)

**Form context: on an account form, "this customer" refers to the open record.**

![Form context](images/chat-form-context.jpg)

**Business glossary: a company term (here "urgent case") is applied and checked in code. The explanation shows each filter and lets the user remove or change it without calling the model again.**

![Glossary term](images/chat-glossary-term.jpg)
![Query explanation](images/chat-query-explanation.jpg)

**E-mail draft: the model writes a template, the code fills in the values. Nothing is ever sent.**

![E-mail draft](images/chat-email-draft.jpg)

**Report finder: opens the CRM's own reports.**

![Report finder](images/chat-report-finder.jpg)

**Turkish questions work the same way.**

![A Turkish question](images/chat-turkish-question.jpg)

**Feedback, slash commands, themes and settings.**

![Feedback form](images/chat-feedback-form.jpg)
![Slash commands](images/chat-slash-commands.jpg)
![Dark theme](images/chat-dark-theme.jpg)
![Maximized window](images/chat-maximized-dark.jpg)
![Appearance settings](images/chat-settings-appearance.jpg)
![Help and shortcuts](images/chat-settings-help.jpg)

## First-run setup wizard

**After `docker compose up` the service starts in setup mode. The one-time code is printed in the service log; the wizard tests the CRM and the model before anything is saved, and secrets are stored encrypted.**

![Setup code](images/setup-1-code.jpg)
![CRM connection](images/setup-2-crm.jpg)
![Model](images/setup-3-model.jpg)
![Administrator and address](images/setup-4-administrator.jpg)

## Administration panel

**Data access: administrators choose which CRM tables and fields the tool may use, on top of the CRM's own security.**

![Data access](images/admin-data-access.jpg)

**Business glossary and documents (company knowledge).**

![Business glossary](images/admin-business-glossary.jpg)
![Documents](images/admin-documents.jpg)

**Quality check: an acceptance set generated from the schema, approved by an administrator.**

![Quality check](images/admin-quality-check.jpg)

**Users, roles and AD groups.**

![Users](images/admin-users.jpg)
![Roles](images/admin-roles.jpg)
![AD groups](images/admin-ad-groups.jpg)

**Audit trail and data access log: who changed what, and which tables were read, never the question text or the data.**

![Audit trail](images/admin-audit-trail.jpg)
![Access log](images/admin-access-log.jpg)

**Design assistant: a requirement written in plain words becomes a customization design that checks the existing schema first. It reads table and field names only and creates nothing in the CRM.**

![Design assistant](images/admin-design-assistant.jpg)

**Export, runtime settings and the identity page.**

![Export](images/admin-export.jpg)
![Settings](images/admin-settings.jpg)
![My identity](images/admin-my-identity.jpg)
