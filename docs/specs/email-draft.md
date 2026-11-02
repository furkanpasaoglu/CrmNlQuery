# Email Draft

Status: Implemented.

## Summary
The user asks for an email about a customer or contact ("write a follow-up email for Acme", or "draft an introduction" while a form is open). The widget shows a draft card with subject, body and recipient, with Copy and Open in email app. Nothing is sent and nothing is written to the CRM.

## Core principle
Row data never reaches the model. A customer-specific email needs names, counts and dates from the CRM, so the **model writes a template with placeholders and code fills in the values**: the model produces subject and body with names like `{{customer_name}}`; code reads the real values with the caller's permissions and substitutes them. The model never sees a value.

## Behavior
1. **Trigger by rule** (`DraftIntent`: an email word plus a write word such as write/draft/prepare and Turkish equivalents, diacritic-folded; query words such as list/get/address cancel it) **and** a single resolved account or contact (by name or open form record). Without a record, a fixed question asks for one (`draft_target`). Follow-up questions and chain steps are never drafts; a summary intent wins when both match.
2. **Placeholder catalog** (fixed in code; the model sees only names and meanings): `customer_name`, `contact_first_name`, `contact_full_name`, `sender_name` (the asking user, read from their own user record), `open_opportunity_count`, `open_opportunity_total`, `nearest_contract_end`, `open_case_count`, `last_activity_date`, `today`. Values come from the same reads as the customer summary (`SummaryBuilder`). Availability comes from the policy-filtered schema: a placeholder that depends on a closed table or field, a zero count, or a denied read is not offered to the model and does not appear in the prompt.
3. **Validation** (`DraftComposer`): every `{{name}}` must be in the catalog and available; no other braces; subject at most 120 characters, body at most 2000; plain text only (no HTML, single-line subject). **No invention:** the prompt forbids facts, names, dates, amounts or references to earlier conversations outside placeholders, and code enforces it: a template containing digits outside placeholders is rejected. Up to 3 attempts with feedback; otherwise a fixed error (`draft_invalid`) and the invalid draft is never shown.
4. **Language and tone:** the request's language; tone is formal, warm or short, read from the question by rule, formal by default.
5. **Filling** (`DraftFiller`, one pass): a value that cannot be found stays as a highlighted `[label]` for the user to fill, never invented.
6. **Recipient:** a contact's `emailaddress1`, or the account's primary contact's address; empty otherwise (the card says so). It is read with the caller's permissions, logged to the access log with source `draft`, and not sent to the model. An account without a primary contact is never given an arbitrary contact.
7. **Card:** a fixed warning ("Draft: check it before sending. Nothing has been sent."), To, Subject and a read-only body; **Copy** (subject and body); **Open in email app** (`mailto:` with subject, body and recipient; if the encoded address exceeds about 1800 characters it is never truncated: the button is disabled and points to Copy); **Draft again**. The text is shown with `textContent`.
8. **History:** a `kind='draft'` entry (`history.payload`) stores a root-record FetchXML and the template JSON with placeholders and no values. Reopening refills the values from fresh CRM data without the model (`DraftService.RefillAsync`). Drafts are ignored for follow-up context, filter editing, Open in CRM, counting, suggestions and voting.

## Security and privacy
- The model sees only the user's own request text and placeholder names; CRM content cannot reach it, so there is no injection path through CRM data.
- Placeholder values obey the data access list; the sender name read is limited to the user's own record and is access-logged.
- Model output is plain text; `mailto:` is built only from the resolved address and encoded text.
- A draft counts as one question for the quota; the time limit applies; the model step is metered as `draft`.

## Settings
`Features:EmailDraft` (default on).

## Future work
Saving the draft as an email activity in the CRM would need a write feature with explicit confirmation, duplicate checks, auditing and a service-account create privilege; it is not part of this feature. Also out of scope: automatic sending, attachments, template libraries, saving user edits, bulk email, an editable body.
