# Schema Change Detection

Status: Implemented.

## Problem

Metadata (table and attribute lists and details) is cached on disk and was refreshed only manually. After a table or attribute is added in the CRM, the chat would not know about it until the cache expired, so a question about the new field produced a wrong answer or a refusal.

## Design

1. **Short cache lifetime (main fix).** `Metadata:CacheMinutes` (default 15, minimum 1, maximum 1440; changeable at runtime under Settings > Planning). After expiry the next use re-reads only the files actually needed (the entity list is one request; a table takes a few). Evaluation runs keep a 24-hour lifetime.
2. **Stale-if-error.** If the CRM is unreachable while re-reading, the expired cache is used instead of failing. A not-found result still returns null; with no cache at all, the error surfaces.
3. **Reactive refresh.** If question analysis cannot pick any table (no primary entity, or not found), `AskPipeline` fully refreshes the metadata once and re-runs the analysis. This happens at most once per 5 minutes across the server, only in the running service (`refreshSchemaWhenUnknown`; off in evaluation). The user notices nothing; that question uses one extra model call.
4. **Admin UI** is unchanged: "Refresh CRM metadata" still refreshes immediately.

## Already correct

- The plan cache key includes the metadata fingerprint, so a schema change retires old plans.
- The analysis cache key includes the entity list.
- The data-access policy shows a new table as closed until an administrator opens it.

## Trade-offs

- A shorter lifetime means more metadata requests to the CRM (roughly the number of used tables times about 10 requests per lifetime); this is accepted.
- A reactive refresh clears all files, so following questions are briefly slower; the 5-minute limit bounds this.

## Settings

`Metadata:CacheMinutes`.

## Out of scope

Change notification from the CRM (webhooks or `RetrieveMetadataChanges`): support differs between versions and the added complexity is not justified.
