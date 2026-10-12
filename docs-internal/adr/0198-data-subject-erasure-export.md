# ADR-0198: Data-subject erasure and export

Status: Accepted

Date: 2026-10-12

## Context

Telemetry could only leave Flare through TTL ([ADR-0143](0143-retention-ttl.md)). A request to delete or hand
over everything about one person (GDPR Art. 17 and 15) had no answer short of editing ClickHouse by hand.
Telemetry has no user table to delete a row from: personal data sits in free-form attribute maps on logs,
spans, span events, metrics and profiles, and in the plain columns of app feedback.

## Decision

- **A subject is a list of `(attribute key, value)` identifiers, matched by exact equality.** `user.id`,
  `user.email`, `client.address` or any other key; a row belongs to the subject if any identifier matches any of
  the table's attribute maps (resource, scope, record/span/data-point/sample, and span-event attributes, which is
  where exception events keep theirs). `app_feedback` also matches its `UserId`/`Email` columns for the
  `user.id`/`enduser.id` and `user.email`/`enduser.email` keys. Empty values are rejected, since `map[key] = ''` is
  true for every row without the key.
- **Sessions are followed one hop, into every table that can carry one.** The session ids are collected from the `session.id`
  attribute of matching logs and spans (capped at 10,000), resolved *before* anything is deleted because the spans
  that name them are among the rows being removed, and then matched with `SessionId IN (...)` in
  `app_screenshots`/`app_feedback` and with `<map>['session.id'] IN (...)` in logs, spans and the four metric tables. A log line
  in the person's session that never repeats the `user.id` is therefore still erased. Profiles carry no session id.
- **Raw telemetry tables only.** The tables of `RetentionSignal` plus `app_feedback`: `logs`, `spans`, the four
  `metrics_*`, `profile_samples`, `app_screenshots`, `app_feedback`. The materialized rollups hold counts and
  quantiles with no identifiers and are left alone, which is also what [ADR-0143](0143-retention-ttl.md) does for TTL.
- **Delete is `ALTER TABLE ... DELETE`, asynchronous and tracked.** `POST /api/erasure` (Admin) counts the matching rows
  per table, inserts a `pending` row in `erasure_operations` (migration 0084) and returns `202`. A
  `BackgroundService` submits one mutation per table with matching rows (`<table>_local ... ON CLUSTER` in cluster
  mode) and records `submitted`; it does not wait for the mutations. Progress is read when asked, from
  `system.mutations` (`clusterAllReplicas` in cluster mode): rows finished, parts left, and the failure reason. The
  request is `success` when every table's mutation is done.
- **Mutations are found by an embedded transaction id.** An `ALTER` that does not wait for its mutation returns no mutation
  id, so each statement adds `AND '<transaction id>' != ''`. The comparison is always true but lands in
  `system.mutations.command`, where `position(command, '<id>')` finds it. Verified against ClickHouse 26.8.
- **Literals, not parameters.** The values are rendered as escaped string literals (backslash and quote
  escaped, control characters rejected) because a mutation's command text is stored and re-run by merges.
- **Dry run and confirmation.** `POST /api/erasure/preview` returns the per-table counts. Erasing requires a
  `confirm` field equal to the first identifier's value.
- **Export is a streamed NDJSON response**, not a stored job: `{"table":"logs","row":{...}}` per line, from
  `SELECT formatRow('JSONEachRow', *)`, capped at 100,000 rows per table with an explicit `truncated` marker.
- **The audit log gets the keys and a hash, never the values.** The values are the personal data, so the audit
  entry and `erasure_operations` store the attribute keys and the first 16 hex characters of a SHA-256 over the
  sorted `key=value` pairs. Two requests for the same subject correlate; the log does not become a second copy of
  what was erased. `erasure_operations` is kept three years as evidence the request was handled.
- **One submission at a time.** A second `POST` while one is `pending` gets `409`. A `pending` row older than 30
  minutes (the API restarted before submitting) is reported as failed and stops blocking.

## Alternatives considered

- **Lightweight `DELETE FROM`.** Rejected for now: it marks rows and hides them from queries but the data stays on
  disk until a merge, which is the wrong guarantee for erasure, and tables with projections (`spans`) restrict it.
  `ALTER ... DELETE` rewrites the parts.
- **Wait for the mutation (`mutations_sync = 2`).** Rejected: a delete on a large table can run for hours, longer than any
  sensible HTTP request, and it gives no progress.
- **Matching `ResourceAttributes` only, or a fixed list of keys.** Rejected: where an app records `user.id` varies
  (resource, span, log, data point), and a fixed list would silently miss data.
- **Storing the identifiers to re-run or verify later.** Rejected as above: it would keep the personal data.

## Consequences

- Erasure rewrites whole data parts, which is I/O heavy on a large table. It runs as ClickHouse mutations, so it
  competes with merges; schedule big ones off-peak.
- Backups, forwarded copies ([ADR-0155](0155-otlp-forwarding.md) targets) and S3 archives are outside Flare and are not touched.
- Personal data that appears only inside a log body or an attribute value under a different key is not matched. The preview is
  how an operator checks what was found, and the pipeline rules are how to stop it being recorded.
- A table whose mutation has been dropped from `system.mutations` (ClickHouse keeps the most recent 100 finished
  ones) is reported finished once two minutes have passed since submission.
- The dashboard page (Settings > Data erasure) is a thin client over the same API: identifiers, preview, export, type-to-confirm erase, and a polled request history.
