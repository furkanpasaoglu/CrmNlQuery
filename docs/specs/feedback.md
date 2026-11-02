# Feedback (Thumbs Up/Down) and Golden-Set Candidates

Status: Implemented.

## Summary
Users can rate each answer with thumbs up or down and, for a thumbs down, pick a reason. Administrators see aggregate quality statistics. A user may optionally share the question with the administrator; shared items become golden-set candidates that are moved to the evaluation set by hand.

Design rules: a thumbs down is not a share; result rows are never stored; nothing is sent to the developer automatically; an administrator sees a user's question text only with the user's explicit, revocable consent.

## Behavior
- After choosing a thumbs down and a reason, an unchecked-by-default box reads "Share this question with your administrator to help improve the tool", with a warning not to share personal information (record names in the explanation are shared too).
- Without sharing, only the reason and metadata (tables, step count, duration, date) are stored; no question text.
- With sharing, the question text and the query explanation text become a candidate. No result rows, FetchXML, GUIDs or user name are stored in the candidate.
- No share box on thumbs up. One vote per result; it can be changed or removed by pressing the selected button again.
- Only answers that return a table are rated (not errors or other outcome kinds).
- Fixed reasons only (no free text, to avoid personal data): `wrong_records`, `wrong_columns`, `misunderstood`, `too_slow`, `other`.
- Deleting a chat or result (single delete, delete all, retention cleanup) withdraws the candidate and removes the user identity from the vote row. The anonymous vote row (with its metadata) stays for statistics until `Metrics:RetentionDays`. A vote without an owner can no longer be changed or withdrawn.
- Vote cleanup does not touch candidates; a candidate lives for `Retention:FeedbackDays` or the life of its history entry, whichever is shorter (pinned chats excepted).

## Data model
- `feedback` (one row per vote): `result_id`, `owner` (nullable after anonymization), `rating` (up | down), `reason`, `shared`, `tables` (names only), `steps`, `duration_ms`, `created_at`, `updated_at`. No question text or rows. A row trigger on `history` anonymizes `owner` on cascade deletes too.
- `feedback_candidates` (shared only): `result_id`, `owner`, `question`, `explanation`, `reason`, `created_at`; foreign key to history with delete cascade. The owner is kept so withdrawal and deletion work; the panel list does not show it.
- `history` gains nullable `steps` and `duration_ms`, filled when the answer is stored (`AnswerOutcome.DurationMs`, not sent to the client).

## Endpoints
- `PUT /api/result/{id}/feedback` `{rating, reason?, share?}`: write or update the vote. `share=true` only with `rating=down` and `Feedback:AllowSharing` on, otherwise 422. Omitting `share` or `false` deletes an existing candidate (withdrawal). Not found, someone else's result, or the feature off: 404.
- `DELETE /api/result/{id}/feedback`: remove the vote and candidate (works even when the feature is off).
- Chat messages carry `feedback {rating, reason, shared}`; `/api/chats` carries `feedback {enabled, allowSharing}`.
- Admin: `GET /api/admin/feedback/summary?days=` (`Dashboard.View`: counts, ratio, reason distribution, tables most often in thumbs down, single- vs multi-step rate, daily series); `GET /api/admin/feedback/candidates` (new permission `Feedback.View`; question, explanation, reason, date; no user name); `DELETE /api/admin/feedback/candidates/{id}`. Every list view is audited as `feedback.view` (who, how many; no text), deletion as `feedback.delete`.
- The write endpoints are allowed from the configured CRM origin by the cross-site guard.

## UI
Widget: small thumbs under each answer; a thumbs down opens an inline form (reason radio buttons, unchecked share box, warning, Send), then "Thanks" and, if shared, "Withdraw sharing". The chat delete confirmation notes that shared feedback is withdrawn too. Panel: a Feedback box on the dashboard and a **Feedback** page (candidate list with Copy and Delete, a note that candidates are shared with consent and never sent automatically). Candidates stay at the customer; an example can be passed on manually with Copy.

## Settings
| Key | Default | Meaning |
|---|---|---|
| `Features:Feedback` | on | Feature switch |
| `Feedback:AllowSharing` | on | Off hides the share box and the server rejects sharing |
| `Retention:FeedbackDays` | 180 | Candidate retention (0 = unlimited) |

## Out of scope
Rating errors, free-text comments, voting on the result page, converting candidates to evaluation cases automatically, sending candidates to the developer.
