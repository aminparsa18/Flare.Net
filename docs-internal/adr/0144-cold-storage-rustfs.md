# ADR-0144: Cold storage as a ClickHouse S3 disk (RustFS), driven by the retention TTL

Status: Accepted

Date: 2026-10-07

## Context

ADR-0143 made retention a TTL on the raw telemetry tables. Deleting aged data is not always what
people want: logs from last quarter are rarely read but expensive to keep on fast local disk. They
want old data on cheap object storage and still queryable.

## Decision

- **Tiering is ClickHouse's own, not an archive Flare manages.** A second disk of type `s3` points at
  a bucket (RustFS in the bundled compose overlays, any S3-compatible store in general), and a
  storage policy `flare_tiered` lists two volumes: `default` (the local disk) and `cold`. A TTL rule
  `... TO VOLUME 'cold'` moves aged parts there. Cold rows stay in the same table, so every query,
  alert and dashboard keeps working; reads from cold are just slower. Verified: a 10-day-old part moved
  to the bucket and `count()` still saw it.
- **The hot volume must be called `default`.** `ALTER TABLE ... MODIFY SETTING storage_policy` only
  accepts a policy containing every volume of the table's current policy, and the implicit policy's
  single volume is named `default`. A volume called `hot` is rejected with `BAD_ARGUMENTS`.
- **`perform_ttl_move_on_insert = 0` on the cold volume.** Without it ClickHouse evaluates the move
  rule against every inserted part on the ingest path once cold storage exists. With it off, moves
  happen in the background.
- **Opt-in, and absent by default.** The disk and policy live in `db/clickhouse/config/cold-storage.xml`,
  mounted only by `docker-compose.cold-storage.yml` (and the `.cluster.yml` variant, which mounts it on
  all four nodes, since a move runs on whichever replica holds the part). Credentials come from the
  container environment through `from_env`, not from the file. A stack without the overlay is unchanged.
- **The policy is assigned per table on demand.** The first request that asks for tiering runs
  `MODIFY SETTING storage_policy = 'flare_tiered'` on each table of the signal (skipped when already
  set), then `MODIFY TTL`. Tables that never tier keep the `default` policy.
- **The retention request grows `coldAfterDays`.** `PUT /api/retention` takes `signals` (delete after)
  plus `coldAfterDays` (move after), per signal. `coldAfterDays` must be less than the retention, unless
  retention is 0, which means tier-only: move to cold, never delete. The TTL is written as
  `<cold rule> TO VOLUME 'cold', <delete rule>`; the parser reads both rules back, so drift is still
  visible. `retention_operations` gains `ColdAfterDays` (migration 0068).
- **`GET /api/retention` reports `coldStorage`**: whether `flare_tiered` has a `cold` volume, plus
  `system.disks` (name, type, free/total bytes), so the UI offers only what is configured. Asking for
  tiering when it is not configured is a `400` naming the overlay to start.

## Alternatives considered

- **Archive to object storage from a Flare worker.** Rejected for the reason ADR-0143 gives for
  `DROP PARTITION`: it duplicates what ClickHouse does, and archived data would no longer be queryable.
- **Always-on RustFS in the base compose files.** Rejected: most installs have no use for it, and a
  required bucket plus credentials is a worse default than none.
- **Make `cold` the only configured policy volume name users can pick.** The roadmap's idea of free-text
  volumes is dropped for now: one fixed `cold` volume keeps the TTL parser exact. Several volumes can be
  added later by reading `system.storage_policies`.

## Consequences

- Moves are background work: after `success`, parts migrate over minutes to hours. The cold disk's
  free/total bytes come from ClickHouse and describe an object store, so treat them as informational.
- Switching a table back off tiering removes the move rule but leaves its policy and any parts already
  on `cold` where they are; they are still read normally and still deleted by the delete rule.
- The Aspire paths have it too: `Flare.AppHost` adds RustFS, a bucket job and the config mount when run
  with `--Flare:ColdStorage=true`, and `AddFlare().WithColdStorage()` in `Flare.Hosting.Aspire` does the same
  for consumers, baking `cold-storage.xml` into the generated ClickHouse image so it publishes. Both use
  plain containers (`rustfs/rustfs`, `minio/mc`) rather than a community RustFS package, gate on RustFS's
  `/health`, and keep the retry loop in the bucket job. Verified live in both.
