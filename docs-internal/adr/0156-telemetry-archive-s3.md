# ADR-0156: Telemetry archive to S3-compatible storage

Status: Accepted

Date: 2026-10-08

## Context

Cold storage (ADR-0144) tiers aged parts to object storage but keeps them in ClickHouse's own part format,
readable only through ClickHouse. People also want a copy in an open format (Parquet, NDJSON) that other
tools can read, and that outlives the retention TTL.

ADR-0144 rejected "archive from a Flare worker" as a way to do tiering. This is a different job: an export
copy, not a replacement for the tier, and the data stays queryable in ClickHouse either way.

## Decision

- **ClickHouse writes the files.** An hourly export runs `INSERT INTO FUNCTION s3(...) SELECT * FROM <table>
  WHERE IngestedAt >= from AND IngestedAt < to`. No rows pass through Flare, and every column is exported,
  including promoted attribute columns. Verified against RustFS for Parquet (strings as UTF-8, maps and
  timestamps round-trip) and gzipped NDJSON.
- **It lives in Flare.AlertWorker** (`ArchiveWorker`), next to the other singleton background jobs, with a
  Redis lock so replicas do not export the same hour twice. It is off unless `Archive:Enabled`.
- **Windows are ingest hours, not event hours.** `IngestedAt` (migration 0011) means a late or back-dated
  event is archived in the hour Flare received it, so a finished hour never changes. An hour is exported
  `Lag` (10 minutes) after it ends, so rows still in the Redis buffer land in it first.
- **Idempotent by construction.** Object keys are deterministic
  (`<prefix>/<table>/dt=YYYY-MM-DD/hh=HH/<table>-<yyyyMMddTHH>00Z.parquet`, or `.ndjson.gz`) and the insert
  uses `s3_truncate_on_insert`, so re-running an hour overwrites it. The only state is one watermark per table
  in Redis; losing it re-exports hours already written. Empty hours write no object.
- **Own bucket settings, not the cold-tier disk.** `Archive:Endpoint/AccessKey/SecretKey/Prefix` are separate
  from `FLARE_COLD_*`: the cold disk's bucket is ClickHouse-managed metadata and must not be shared with
  files other tools read. The same RustFS instance can serve both with different buckets.
- **Scope.** Signals `Logs` (`logs`), `Traces` (`spans`) and `Metrics` (the four `metrics_*` tables). Profiles
  and config tables are not archived. The first run starts at the current hour unless `Archive:StartFrom` asks
  for a backfill, taken `MaxWindowsPerPoll` hours at a time.

## Consequences

- Data already past retention before the archive was enabled is not exported, and rows deleted by TTL before
  their hour is exported are lost to the archive (the lag is short; keep retention above a day).
- In cluster mode the export runs once, on the initiator, against the Distributed table.
- Credentials sit in the worker's environment and in the SQL text sent to ClickHouse (and so in its query log).
