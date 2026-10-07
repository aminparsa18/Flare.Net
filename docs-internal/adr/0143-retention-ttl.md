# ADR-0143: Data retention as ClickHouse TTLs, applied asynchronously

Status: Accepted

Date: 2026-10-07

## Context

Flare kept every row forever. The `logs`/`spans`/`metrics_*`/`profile_samples` tables carried no TTL
on purpose (their migrations said so) until retention had a design. A self-hosted install needs a
way to bound disk use, and the later cold-storage work (RustFS, tiered volumes) needs the same
mechanism: a TTL on the table that ClickHouse evaluates itself.

## Decision

- **Retention is a per-signal TTL on the raw telemetry tables, owned by ClickHouse.** Four signals:
  `logs` (`logs`), `traces` (`spans`), `metrics` (the four `metrics_*` tables) and `profiles`
  (`profile_samples`). Each table's TTL is `toDateTime(<time column>) + toIntervalDay(N)`. Nothing in
  Flare deletes rows; ClickHouse drops expired parts during background merges. The pre-aggregated
  rollup tables (`service_metrics`, `outbound_calls`, ...) and config tables keep no TTL: they are
  small, and expiring a rollup before its source would make the two disagree.
- **0 days means keep forever** (`REMOVE TTL`). That is also the default: an upgrade never deletes
  data nobody asked to delete.
- **`MODIFY TTL` runs with `materialize_ttl_after_modify = 0`.** Otherwise every retention change
  rewrites every existing part up front. With it off, existing parts are re-evaluated as merges
  touch them, so a shortened retention frees disk gradually instead of as one large mutation.
- **In cluster mode the statement targets `<table>_local ... ON CLUSTER 'flare_cluster'`.** A
  `Distributed` table cannot carry a TTL (ADR-0003).
- **Applying is asynchronous and tracked.** `PUT /api/retention` (Admin) writes a `pending` row per
  signal to `retention_operations` (migration 0067) and returns `202` with a transaction id. A
  `BackgroundService` in `Flare.Api` runs the `ALTER`s and writes `success` or `failed` as a newer
  version of the same row. A second `PUT` while one is `pending` is rejected with `409`; mutations are
  not queued behind each other. A `pending` row not updated for 30 minutes (the API restarted
  mid-apply) is reported as failed and stops blocking.
- **`GET /api/retention` reports actual and expected separately.** *Actual* is parsed live from
  `system.tables.create_table_query`, so a TTL edited by hand shows up as `custom` instead of being
  hidden behind what Flare last wrote. *Expected* is the last requested value with its status. The
  dashboard can show "applying..." instead of a stale number, and drift is visible.
- **`retention_operations` is a `ReplacingMergeTree(UpdatedAt)` keyed by `(TransactionId, Signal)`**,
  read with `LIMIT 1 BY` rather than `FINAL` (ADR-0074), and kept for a year.

## Alternatives considered

- **`DROP PARTITION` on a schedule from a Flare worker.** Rejected: the tables are partitioned by
  month, so it can only drop whole months, and it makes Flare responsible for a job ClickHouse already
  does. A TTL is exact to the row and survives Flare being down.
- **`ttl_only_drop_parts = 1`.** Not enabled. It drops a part only when every row in it has expired,
  which is cheaper but can hold data well past its retention when a part spans the boundary. Worth
  revisiting if merges on large installs prove costly.
- **Blocking `PUT` until the `ALTER` finishes.** Rejected: in cluster mode it is a distributed DDL
  round trip that can take minutes, longer than a sensible HTTP timeout.
- **Storing the expected value in a settings table separate from the history.** Rejected as a second
  source of truth; the latest operation per signal is the expected value.

## Consequences

- Shortening retention is destructive and is applied by ClickHouse in the background, so disk frees up
  over hours, not at the moment `success` is reported. `success` means the TTL is in place.
- Two API replicas can each hold a different in-memory queue. The `409` check reads the shared table,
  so concurrent requests are still rejected, but a job queued in a replica that then dies is only
  recovered by the 30-minute staleness rule and a new request.
- Per-resource retention rules and the cold-storage volume build on this: both only change what the
  TTL expression and clause are (`multiIf(...)` days, `TO VOLUME`). See the roadmap.
