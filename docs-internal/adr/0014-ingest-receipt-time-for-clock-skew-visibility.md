# ADR-0014: Server receipt time (`IngestedAt`) for clock-skew visibility

Status: Accepted
Date: 2026-08-31

## Context

Planning's roadmap carried an open item since before v1: "Timestamp/timezone &
clock-skew handling from distributed clients," deferred with "no clock-skew
compensation exists anywhere in the ingest pipeline today; timestamps are stored
as received on the wire," to be revisited "if multi-region/clock-drift-sensitive
deployments become a reported problem."

Investigating it turned up a more basic gap than "no compensation exists yet":
**Flare cannot compute clock skew at all today**, compensated or not. OTLP's
`LogRecord` carries two timestamps - `time_unix_nano` (event time) and
`observed_time_unix_nano` ("time when the event was observed by the collection
system") - and `Flare.Ingest`/ClickHouse already store both
(`LogEvent.Timestamp`/`ObservedTimestamp`, `db/clickhouse/0001_logs.sql`). But in
every .NET OTel SDK export path Flare's receivers actually see, both fields are
stamped from the **same client-side clock**, generally at the same instant - the
SDK's log processor sets `observed_time` when it processes the log call, not when
Flare's receiver later reads the request off the wire. Comparing
`ObservedTimestamp` to `Timestamp` tells you almost nothing about *this server's*
clock relative to the client's; it mostly measures how long the SDK's own
in-process log pipeline took.

To detect skew against something trustworthy, Flare needs a timestamp from its
*own* clock, read at the moment it actually receives a batch - which it has never
captured anywhere in the ingest pipeline.

## Decision

**Capture `IngestedAt`: `Flare.Ingest`'s own wall-clock read (`TimeProvider.GetUtcNow()`),
taken once per accepted OTLP export request, on all three signals (logs, spans,
metrics).** Stored as a new column alongside each table's existing event-time
column(s) - `logs.IngestedAt`, `spans.IngestedAt`,
`metrics_gauge/sum/histogram.IngestedAt` - never replacing them.

**Never rewrite the stored event timestamp.** `Timestamp`/`StartTime`/`Time` keep
being stored exactly as the client sent them, same "store what's on the wire"
philosophy the rest of the pipeline already follows (`LogEvent`'s own remarks on
proto3 empty-string handling make the same call for strings). `IngestedAt` is
purely an additional, independently-trustworthy reference point - it is Flare's
answer to "when did I actually see this," kept separate from "when did the client
say this happened."

**Sign convention:** `SkewNanos = IngestedAt − eventTime`. Positive = the event
claims a time in the past relative to receipt (the expected case - network and
processing latency). Negative = the event claims a time in the future relative to
receipt (the interesting case - the sending client's clock is genuinely ahead of
this server's).

**Surface it, don't act on it (v1 scope):** a per-service average clock-skew
figure is computed from `IngestedAt − event time` at ingest time, tracked in the
same Redis per-minute/per-service stats structure `RedisIngestionStatsTracker`
already maintains for records/bytes (`ServiceBreakdown`,
`IngestionStatsKeys.ServiceRecordsKey`/`ServiceBytesKey`), and rendered as a new
column on the Ingestion Health page's existing "Services by signal" table
(`PipelineServiceBreakdown.svelte`). Purely visibility - an operator can now see
"service X's clock differs from this server's by ~90s" instead of having no way to
know.

## Alternatives considered

- **Compensate automatically** (adjust `Timestamp` toward the server's clock when
  skew is detected). Rejected: silently rewriting the one field a user explicitly
  sent as "when this happened" is a bigger, harder-to-reverse decision than this
  ADR is trying to make, and wrong by construction whenever the skew is real (a
  genuinely delayed/backfilled/replayed event looks identical to clock skew from
  the server's point of view - there's no way to tell them apart without more
  context than one timestamp comparison gives). If automatic correction is ever
  wanted, it needs its own ADR and its own explicit opt-in, not to ride in on this
  one.
- **Wire skew into an alert rule / notify on threshold.** Rejected for v1 -
  visibility first; whether skew crossing some threshold deserves a Slack/webhook
  page is a real product decision (what threshold, per-service or global, does a
  transient blip matter) better made once the visibility feature shows what real
  skew looks like in practice, not guessed at alongside it.
- **Use `ObservedTimestamp` as the skew reference instead of adding a new
  column.** Rejected per the Context section above - it's a client-clock value in
  every path that matters, so it doesn't answer the question.
- **Compute skew per-record rather than per-batch, or store it per-row instead of
  aggregating in Redis.** Rejected for v1: `IngestedAt` is captured once per
  export request (all records in a batch share it), which is precise enough for a
  service-level operator signal and keeps the mapper pure/deterministic (the
  receipt time is passed in as a parameter, not read from a clock inside the
  mapper). A per-row skew column/query surface is a larger, separate feature if
  ever needed - the plain `IngestedAt` column this ADR adds is enough raw data to
  build that later without another migration.

## Consequences

- Five `ALTER TABLE ... ADD COLUMN` migrations (`0011_ingest_receipt_time.sql`,
  mirrored in `db/clickhouse-cluster/`), no backfill for existing rows (same
  precedent as `0002_logs_event_id.sql`/`0010_logs_pattern.sql`).
- `LogEvent`/`SpanRecord`/`MetricPointRecord`, their OTLP mappers, and all six
  OTLP receivers (gRPC + HTTP × logs/spans/metrics) take a small, mechanical
  signature change to plumb `ingestedAt` through - see each type's own remarks.
- The per-service skew figure is a simple arithmetic mean per minute bucket (sum ÷
  record count), computed the same way `ServiceBreakdown`'s existing byte-share
  figure is documented as an approximation - sensitive to one wildly-wrong record
  skewing the average, not a robust statistic. Acceptable for an operator-facing
  visibility signal; revisit if it proves too noisy in practice.
- No change to `/api/logs/search`'s default lookback, keyset pagination, live-tail
  windowing, or alert evaluation - none of those read `IngestedAt` in v1. A
  severely clock-skewed client's events can still land outside a query's default
  time window exactly as before this ADR; that's the "query/alerting" follow-up
  explicitly deferred above, not solved here.
