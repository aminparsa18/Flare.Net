# ADR-0268: Browser replay: byte limits of its own

Status: Accepted

Date: 2026-10-21

## Context

[ADR-0265](0265-browser-replay-follow-ups.md) counted a replay chunk's decompressed size against the ingest key's existing byte
caps ([ADR-0051](0051-per-ingest-key-ingestion-limits.md)) and left a replay-specific cap on the roadmap for when the shared
ones proved too coarse. They are coarse in one direction that needs no evidence: a key that sends logs, traces and replay has a
single byte budget, and replay is by far the largest consumer (one page snapshot is hundreds of kilobytes, and canvas frames
add up to 25 MB a session). An operator who wants "at most 2 GB of replay a day" can only get it by capping everything.

## Decision

- **Two more optional caps on a key: `MaxReplayBytesPerMinute` and `MaxReplayBytesPerDay`.** Same semantics as the existing
  four: null is no cap, a set cap must be positive, and `Enabled` switches the whole set on and off. Migration `0037` (Sqlite)
  and `0012` (Postgres) add two nullable columns; every existing key keeps null.
- **They bound replay only, and replay stays inside the shared caps.** A chunk is added to the key's shared bytes (as in
  ADR-0265) and to a third Redis hash field, `replay_bytes`, in the same minute and day hashes. A `POST /v1/replay` request is
  refused when either its shared byte cap or its replay cap is reached; any other request ignores the replay caps
  (`IngestKeyLimitEvaluator.Evaluate(..., isReplay)`, decided by the path in `IngestKeyLimitRejection.IsReplay`). So a replay cap
  is an extra ceiling on one kind of traffic and replay can never starve OTLP of more than the shared cap allows, but it cannot
  be raised above the shared one either. Making replay a wholly separate budget would have meant uncapped replay for every
  key that already relies on its byte cap, an upgrade that changes behaviour silently.
- **A refusal is the same `429` + `Retry-After`** the recorder already honours, with the daily cap winning over the minute cap.
- **No new counter key.** `replay_bytes` is a field in the existing hashes, written only when non-zero, so a key that never
  sees replay costs nothing more. The API reads it for the Ingest Keys page (`ReplayBytesThisMinute`, `ReplayBytesToday`).
- **Surfaces.** `PUT /api/ingest-keys/{id}/limits` takes the two caps, `IngestApiKeyDto` appends them and the two usage numbers
  (the hand-written TypeScript decoder is read in declared order, so they go last), the limits dialog has a "Replay bytes" row,
  the table shows `Replay used / cap` under a window's usage when a replay cap is set, and `flare config export`/`apply` carry
  `maxReplayBytesPerMinute` and `maxReplayBytesPerDay`.

## Consequences

- An operator can bound replay storage per key without touching the key's log, trace and metric budget.
- A key with only a replay cap set is "enforced", so Ingest reads its usage from Redis on every request, as for any limited key.
- The Terraform provider's `flare_ingest_key` resource does not have the two attributes yet; they are accepted by the API
  and a plan that omits them clears them, since `PUT .../limits` replaces all seven.
- Request method, headers and bodies for the network list ([ADR-0266](0266-browser-replay-console-network.md)) stay open.
