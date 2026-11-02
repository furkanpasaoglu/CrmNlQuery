# Multilingual UI (Turkish / English)

Status: Implemented.

## Summary
Every user-visible text can be shown in Turkish or English: the chat widget, the result page, the admin panel, and every message or label coming from the server. Language is a per-user preference. A cheap catalog mechanism plus tests keep new features from shipping untranslated text.

## Decisions
1. Languages: `en` and `tr`; the infrastructure is ready for a third.
2. Selection: a "Language" entry in the widget's settings menu, stored per user like the address name. The admin panel uses the same preference and can change it too. Without a preference: the browser language (`tr*` -> tr, else en), then `Ui:DefaultLanguage` (default `en`).
3. Scope covers the widget through the admin panel.

## Principles
- **One catalog rule:** no user-visible text is embedded in code; each surface has its own catalog. English is the source language and Turkish is mandatory.
- Keys are stable (`widget.sumTitle`, `msg.contextOverflow`); parameters use `{name}`.
- Avoid language-dependent grammar: write count-independent sentences; where a plural is needed use `key.one` / `key.other`.
- **Not translated:** all model prompts (they stay English), logs, the content of audit and access records (stored data must not depend on language), CRM table/field/option labels (the CRM supplies them in its own language), API field names, error codes.
- **The UI language is never sent to the model.** Text the model writes (a drafted e-mail, a design document) follows the language of the question or request.
- A request without a language gets `en` (evaluation and tests are unchanged).

## Design
### Language resolution and preference
- `UiLanguage`: `en` | `tr`; unknown values fall back to the default.
- Preference: table `user_language` keyed by account; `GET/PUT /api/me/preferences` accepts `language` (invalid value 422); the admin panel uses `PUT /api/admin/me/language`.
- Resolution order (`LanguageResolver`): user preference -> `Accept-Language` -> `Ui:DefaultLanguage`. Middleware resolves it on every `/api` request (`RequestLanguage`); clients pick their own catalog using the same result.
- `<html lang>` and the `Intl` locale (`tr-TR` / `en-US`) follow the language, so dates, numbers and month names (charts included) are formatted in the browser.

### Client catalogs
- **Widget:** `T = { en: {...}, tr: {...} }`; `t(key, params)` falls back TR -> EN -> the key itself, never an empty string. Tests check key and `{parameter}` parity.
- **Result page (`result.js`):** its own small catalog; the shared `explainText` block is identical in the widget and the result page (a test enforces this) and takes a language parameter.
- **Admin panel:** a core i18n module plus per-page catalogs; menu titles, buttons, table headers, empty states and confirmation dialogs all come from the catalog.

### Server texts
- **Messages to users** (`UserMessages`, failure and rejection messages, quota and concurrency rejections): `Msg` keys and `MessageCatalog` hold both languages; messages are produced in the ambient request language. `FailureOutcome.FromKey` and `OutcomeDto` carry the translated text.
- **In-answer labels** (summary section and metric titles, suggestion labels, narrowing hints, fixed sentences of the query explanation, draft placeholder labels) are produced in the request language by the server, except CRM-supplied labels, which are never translated.
- **Admin API messages** (`AccessResult.Fail`, knowledge base, settings, data policy, export, design) come from the server catalog (`Msg.Adm*`) in the request language; validation errors that go to the model and audit `detail` text stay English.
- **Setting descriptions, permission descriptions and export column descriptions** are translated in the panel catalog; the server definition carries an English fallback. Export column names stay English.

### Enforcement
- Parity tests: widget `T` en vs tr, every admin page catalog, every server message in both languages with equal parameters, every setting, permission and export column has a panel translation.
- A scanner (`tools/Scan-UiText.js`) counts embedded user-facing literals not in a catalog; a ceiling test (`ui-text.test.js`) fails on new ones, with a short documented exception list.
- Rule for every new feature: new user-visible text goes into the catalogs, EN and TR together; a new setting needs a `setting-desc` row, a new permission a `permissions` row, a new export column an `export-columns` row.

## Adding a language
Add the language to `UiLanguage`, add its dictionary to `MessageCatalog`, add `T_xx` to the widget, a catalog to `result.js`, a column to every per-page panel catalog, and to the panel's supported-language list.

## Known behavior
- Suffixes appended to a stored question text ("(edited: ...)", "(then: ...)") stay English because they are parse markers.
- Example questions are data: `Widget:ExampleQuestions` and `Widget:ExampleQuestionsTr` (Turkish requests use the Turkish list, falling back to the general list; the same lists feed the "you could ask, for example" hints).
- Turkish text is typically 20-30% longer; layouts are checked in both languages.

## Settings
| Key | Default |
|---|---|
| `Ui:DefaultLanguage` | `en` |
| `Widget:ExampleQuestionsTr` | empty |

## Out of scope
A third language, right-to-left languages, translating model prompts, forcing the question language to match the UI language, translating knowledge-base content, CRM labels, or the content of audit and access records.
