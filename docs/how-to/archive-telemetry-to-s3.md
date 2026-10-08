# Archive telemetry to S3-compatible storage

Flare can write finished hours of logs, traces and metrics to a bucket as Parquet or gzipped NDJSON, for
long-term keeping or for analysis in tools such as DuckDB, Athena or Spark. The data stays in Flare too;
the archive is a copy. To keep old data *queryable* on cheap storage instead, see
[Set data retention](set-data-retention.md).

## Turn it on

Create a bucket (for example in RustFS or MinIO), then set these on **Flare.AlertWorker**. With the
standalone compose stack, put them in `.env`:

```bash
FLARE_ARCHIVE_ENABLED=true
FLARE_ARCHIVE_ENDPOINT=http://rustfs:9000/flare-archive   # path-style bucket URL
FLARE_ARCHIVE_ACCESS_KEY=...
FLARE_ARCHIVE_SECRET_KEY=...
```

Use a different bucket from the cold-storage one.

| Setting (`Archive__…`) | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Master switch. |
| `Endpoint`, `AccessKey`, `SecretKey` | required | Bucket URL and credentials. |
| `Prefix` | `flare` | Key prefix inside the bucket. |
| `Format` | `Parquet` | `Parquet` or `Ndjson` (gzip, one JSON object per line). |
| `Signals__0…` | all | Any of `Logs`, `Traces`, `Metrics`. |
| `StartFrom` | current hour | Ingest time to begin from on the first run, to backfill. |
| `Lag` | `00:10:00` | How long after an hour ends it is exported. |
| `PollInterval` | `00:05:00` | How often to look for finished hours. |
| `MaxWindowsPerPoll` | `6` | Hours exported per table per poll while catching up. |

## What gets written

One object per table per hour, laid out so query engines can prune by date:

```
flare/logs/dt=2026-10-08/hh=14/logs-20261008T1400Z.parquet
flare/spans/dt=2026-10-08/hh=14/spans-20261008T1400Z.parquet
flare/metrics_gauge/dt=2026-10-08/hh=14/metrics_gauge-20261008T1400Z.parquet
```

Every column is included. Hours are by the time Flare *received* the data (`IngestedAt`), so a delayed or
back-dated event lands in the hour it arrived. Hours with no data write no object. A failed hour is retried
on the next poll and re-writing an hour overwrites it, so retries never duplicate data.

Read it back, for example with DuckDB: `SELECT * FROM read_parquet('s3://flare-archive/flare/logs/*/*/*.parquet')`.

## Limits

- Data older than the moment you enable the archive is not exported unless you set `StartFrom`, and only while
  it still exists in Flare.
- Profiles and Flare's own configuration tables are not archived.
- The credentials are sent to ClickHouse inside the export statement, so they appear in its query log.

Design notes: [ADR-0156](../../docs-internal/adr/0156-telemetry-archive-s3.md).
