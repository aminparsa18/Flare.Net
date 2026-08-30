# Investigation: ingest throughput + query latency benchmark

Date: 2026-08-22 (Planning.md's "Multi-node scaling" item)
Related: ADR-0002 (Redis Streams buffering), ADR-0003 (Distributed tables
+ spans sharding), `db/clickhouse/README.md` (schema this benchmark
exercises)

A proof point for a claim made elsewhere in this repo's own design
discussions: that Flare inherits horizontal-availability/scale
characteristics from ClickHouse + Redis Streams "for free," rather than
needing a bespoke, separately-licensed clustering subsystem the way a
self-contained embedded log-store product does. This document measures two
concrete numbers instead of asserting the claim on architecture narrative
alone:

1. **Ingest throughput** - how fast the OTLP → Redis Streams → ClickHouse
   pipeline actually sustains, under a saturating producer.
2. **Query latency** (p50/p95) - across query-pattern categories the logs
   table's own schema comments (`db/clickhouse/0001_logs.sql`) already
   predicted would behave differently.

## Methodology & environment

**This is a single-machine, local dev-laptop benchmark, not a
cloud/production number.** Every result below should be read as "what a
modest local Docker Compose deployment does," not as a datacenter or
multi-node claim.

- **Host**: Intel Core i5-1038NG7 @ 2.00GHz (4 cores / 8 threads), 16 GB
  physical RAM, macOS.
- **Docker Desktop VM**: 8 vCPUs, ~7.65 GB RAM allocated.
- **`docker-compose.yml` sets no per-container CPU/memory limits** -
  `clickhouse`, `redis`, `ingest`, `api`, and `dashboard` all compete
  freely for the pool above. There is no dedicated-resource-per-service
  story here; that's a real caveat on every number below, not an
  oversight.
- All five services running from a fresh `docker compose up` (no
  artificial warm cache beyond what a normal `docker compose up` gives
  you).
- Ingest pipeline config: `Flare.Ingest`'s defaults, unmodified
  (`BatchSize=1000`, `FlushInterval=2s`, single `ClickHouseFlushWorker`,
  single Redis Streams consumer - see
  `src/Flare.Ingest/Pipeline/LogEventPipelineOptions.cs`).
- Producer: a standalone `ExampleApp.LogGenerator` process (not
  containerized, run directly on the host against `localhost:4317`), using
  the real `Aspire.Flare`/`AddFlareOtlpExporter` OTLP exporter every real
  integration uses - not a hand-rolled wire-level client.

## Finding 1: ingest throughput

**Setup**: `POST /generate-throughput?durationSeconds=15&concurrency=N`
(new, benchmark-only endpoint added to `ExampleApp.LogGenerator`)
saturates the pipeline for 15 seconds at N concurrent producer tasks, no
pacing delay. Measured by direct ClickHouse `count()` before/after each
run (+3s grace period to catch the last flush cycle), not a client-side
send count - the ground truth for "what actually landed," since this
pipeline buffers through Redis Streams and flushes on its own schedule,
not synchronously with the call that wrote to the stream.

| Concurrency | Client-attempted `LogInformation()` calls | Rows landed in ClickHouse (15s) | Effective rate |
|---|---|---|---|
| 1  | 1,244,594 | 16,000 | ~1,067 events/sec |
| 4  |   651,519 | 15,000 | ~1,000 events/sec |
| 16 |   449,780 | 16,000 | ~1,067 events/sec |

**The pipeline sustains ~1,000-1,100 events/sec end-to-end, and this
number does not change with producer concurrency.** That flatness is
itself the interesting result - it means a single serialization point
somewhere in the pipeline is the real ceiling, not client-side thread
contention.

Two things worth being precise about, rather than picking one and
overclaiming:

- **The "attempted" column falling as concurrency rises is not a
  throughput measurement of Flare at all** - it reflects the .NET
  OpenTelemetry SDK's own client-side `BatchLogRecordProcessor`, which
  uses a bounded queue (default `MaxQueueSize=2048`) and silently drops
  new records once full rather than blocking the caller. Neither
  `Flare.ServiceDefaults` nor `Aspire.Flare` override these defaults
  (confirmed: no `BatchExportProcessorOptions` configuration anywhere in
  either project). At higher concurrency, more threads are racing to fill
  the same bounded queue, so more individual `LogInformation()` calls get
  silently dropped before ever being queued for export - this is why
  "attempted" *drops* as concurrency rises, not because logging itself got
  slower.
- **What's actually gating the ~1,000-1,100/sec ceiling** - the client
  SDK's own batch export cadence, or `Flare.Ingest`'s single-threaded
  pipeline (`RedisStreamLogEventSink.WriteAsync` issues one un-pipelined
  `XADD` per event; `ClickHouseFlushWorker` is one sequential
  `BackgroundService` with no horizontal fan-out) - **wasn't conclusively
  isolated by this benchmark**, and shouldn't be claimed as isolated.
  `XLEN`/`XPENDING` checks during the run showed no large Redis Stream
  backlog building up, which is more consistent with the client SDK's
  export cadence being the binding constraint than with `Flare.Ingest`
  falling behind a firehose it can't drain - but confirming that precisely
  needs a client that bypasses the SDK's batching entirely (a raw OTLP
  client sending large pre-batched export requests), which is a real,
  named follow-up, not done here.

**What this means operationally**: a single default-configured OTel SDK
producer process should not be expected to sustain much more than
~1,000-1,100 events/sec into Flare without either tuning the SDK's own
`BatchExportProcessorOptions` (larger queue/batch size) or running
multiple producer processes. This is useful, actionable guidance for
anyone deploying Flare at real volume - and it's a client-side tuning
lever, not evidence of a hard Flare-side ceiling.

## Finding 2: query latency

**Setup**: `scripts/seed-benchmark-logs.py` bulk-loaded 5,000,000
synthetic rows directly into `clickhousedb.logs` via ClickHouse's HTTP
interface (bypassing the ingest pipeline entirely - seeding through Redis
Streams one-by-one would conflate seeding cost with what's being
measured), spread over a 24-hour synthetic window across 10 services,
realistic severity distribution (skewed Info/Warn with an Error tail), and
a bounded set of templated messages (so `idx_body`'s `tokenbf_v1` skip
index has real repeated substrings to hit, the same way real application
log traffic clusters into a bounded number of shapes). Seeded at ~11,000
rows/sec (452s total) - itself informative: that's roughly **10x this
benchmark's own measured ingest throughput**, which is expected and
correct, since a direct ClickHouse bulk `INSERT` skips the OTLP/Redis
Streams/batching pipeline entirely - it is not a second ingest throughput
number, just confirmation the seeding step didn't become the bottleneck.

`scripts/query-latency-benchmark.py` then ran 6 query-pattern categories -
chosen because the schema's own doc comments
(`db/clickhouse/0001_logs.sql:111-123`) distinguish them - as real HTTP
calls against `POST /api/logs/search` / `POST /api/logs/aggregate` (not
raw ClickHouse queries, so JSON serialization/HTTP overhead is included,
matching what the dashboard itself experiences), 30 iterations per pattern
after 3 warmup calls:

| Pattern | Access path | p50 | p95 |
|---|---|---:|---:|
| (a) service + time range | `ORDER BY` prefix (`ServiceName`) | 82.0ms | 135.4ms |
| (b) service + severity + time range | `ORDER BY` prefix, further narrowed | 63.0ms | 108.3ms |
| (c) `TraceId` exact match | `idx_trace_id` bloom_filter skip index | 28.7ms | 58.4ms |
| (d) log attribute filter, no service scope | map skip index, no `ORDER BY` locality | 337.1ms | 409.5ms |
| (e) `Body` substring search, no service scope | `idx_body` tokenbf_v1 skip index, no locality | 271.1ms | 342.9ms |
| (f) unfiltered all-services aggregate | `GROUP BY`, no `ORDER BY` locality | 197.4ms | 228.1ms |

**A genuine surprise worth stating plainly rather than smoothing over**:
the schema's own comment names the all-services case (pattern f) as the
deliberately unresolved worst case - "if that all-services view proves too
slow... the named follow-up is a Timestamp-first projection." At this
dataset size, **it wasn't the worst case**. The two unscoped
`WHERE`-filtered searches (d, e) were both slower than the unscoped
`GROUP BY` aggregate (f) - plausibly because (d)/(e) still have to
materialize and return up to 200 individual matching rows via skip-index
lookups across the full 5M-row, unscoped dataset, while (f) only returns a
small number of time-bucket rows even though it scans the same breadth.
This doesn't mean the schema's named concern is wrong in general (a much
larger dataset, or a less selective attribute/substring filter, could
still tip it) - it means *this specific* predicted worst case isn't the
one that showed up first at 5M rows, and the Timestamp-first-projection
follow-up that comment named isn't the most urgent lever based on this
data. The cheap end of the table (a, b, c) confirms the `ORDER BY` design
is doing its job: scoping to one service, or hitting a skip-indexed exact
match, is 3-10x cheaper than an unscoped scan.

## Reproducing this

```sh
docker compose up -d
ConnectionStrings__flare="http://localhost:4317" dotnet run --project examples/ExampleApp.LogGenerator --no-launch-profile &

# Ingest throughput (repeat at whatever concurrency levels you want):
curl -s -X POST "http://localhost:5000/generate-throughput?durationSeconds=15&concurrency=4"

# Query latency:
python3 scripts/seed-benchmark-logs.py --rows 5000000
python3 scripts/query-latency-benchmark.py --trace-id <a real TraceId from your seeded data> --service benchmark-seed-svc-3
python3 scripts/seed-benchmark-logs.py --cleanup   # removes only benchmark-seed-* rows, restores baseline
```

## Conclusion

The pipeline sustains ~1,000-1,100 events/sec from a single default-
configured OTel SDK producer, a client-side tuning ceiling rather than a
confirmed Flare-side one. Query latency confirms the `ORDER BY` design's
cheap path (service-scoped, skip-indexed) is 3-10x faster than an unscoped
scan, and surfaced that the schema's own predicted worst case (unscoped
all-services aggregate) was not, in fact, the slowest pattern at 5M rows —
the two unscoped `WHERE`-filtered searches were slower.

## Unresolved / follow-ups

- Whether the ~1,000-1,100/sec ceiling is gated by the client SDK's batch
  export cadence or by `Flare.Ingest`'s single-threaded pipeline
  (`RedisStreamLogEventSink`'s un-pipelined `XADD`, `ClickHouseFlushWorker`'s
  lack of horizontal fan-out) was not conclusively isolated — needs a
  client that bypasses SDK batching entirely (a raw OTLP client sending
  large pre-batched export requests).
- Whether the schema's named "Timestamp-first projection" follow-up for
  the unscoped-aggregate case is still the right next lever, given that at
  5M rows the two unscoped `WHERE`-filtered searches were the actual worst
  case, not the aggregate.
- Explicitly out of scope for this investigation, not just unresolved:
  fixing or optimizing anything the numbers surfaced (this is measure-and-
  report, not a decision to act); RustFS retention/cold storage; any
  multi-node, multi-process-producer, or cloud-hardware numbers.