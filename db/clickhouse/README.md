# ClickHouse schema

Flare's storage layer. This directory is the shared-storage contract between
`Flare.Ingest` (writes, via the batched insert pipeline - a later roadmap item) and
`Flare.Api` (reads, not yet built) - it deliberately doesn't live under `src/Flare.Ingest`
for that reason.

## What's here today

`0001_logs.sql` - the `logs` table: one row per OTLP `LogRecord`, mirroring
[`Flare.Ingest`'s `LogEvent` model](../../src/Flare.Ingest/Model/LogEvent.cs) 1:1. This
is the roadmap item **"Internal log-event model + ClickHouse schema"** - it resolves
Planning.md's open question #2 ("Map vs. JSON vs. dynamic columns... query performance
depends on getting this right").

## What it deliberately does *not* do (yet)

- No ClickHouse-writing code anywhere - `Flare.Ingest`'s `ConsoleLogEventSink` is
  untouched. The batched insert pipeline (a separate roadmap item) is what actually
  writes to this table.
- No query/search/aggregate/live-tail surface - that's the Query API roadmap item.
- No `docker-compose.yml` apply story - the AppHost init-mount below only solves local
  Aspire-based dev; the `docker-compose.yml` v1 roadmap item needs to close this gap for
  non-Aspire environments (probably the same bind-mount trick against the official
  image).
- No materialized views for pre-aggregated charts, and no `TTL` clause - see the design
  decisions below.

## Design decisions

**Attribute typing.** Every OTLP `AnyValue` variant (string/bool/int/double/bytes/array/
kvlist) is flattened to `string` at ingest (`Flare.Ingest`'s `OtlpLogMapper`), landing in
one of three ClickHouse `Map(LowCardinality(String), String)` columns -
`ResourceAttributes`, `ScopeAttributes`, `LogAttributes` - mirroring OTel's three
attribute bags (resource, instrumentation scope, log record). This is the same approach
the OpenTelemetry Collector's own ClickHouse exporter and SigNoz use: proven, not
reinvented.

**Single `logs` table**, `MergeTree` engine - log events are immutable and never
updated, so there's no need for `ReplacingMergeTree`/dedup. `PARTITION BY
toStartOfMonth(Timestamp)`, not daily: the ClickHouse `clickhouse-best-practices` agent
skill's `schema-partition-low-cardinality` rule flags exactly `PARTITION BY
toDate(timestamp)` as its own textbook mistake (unbounded partition growth over years).
Flare is a long-running self-hosted service with no v1 TTL yet to bound that growth, so
monthly partitions keep the count in the recommended 100-1,000 range for years, while
still setting up for `schema-partition-lifecycle`'s stated purpose (day-to-month-grained
`DROP PARTITION`-based retention) once the "Later" roadmap's retention item lands.

**No `TTL` clause.** Planning.md's roadmap lists "Retention policies + cold storage to
S3-compatible object store (RustFS)" as a separate "Later" item. A hardcoded TTL now
would preempt that design - deliberate omission, not an oversight.

**No materialized views.** The dashboard's "simple charts" (volume over time, by level,
by service) can run plain `GROUP BY` queries against `logs` for v1. Whether
pre-aggregation is worth the complexity is a decision for the Query API roadmap item,
once real query latency against real data is known.

**`ORDER BY (ServiceName, SeverityNumber, Timestamp, TraceId)`** - low-to-high
cardinality, leading with the two columns the dashboard filters by most (a dev viewing
one service's logs, then narrowing by severity). **Known, deliberately unresolved
trade-off:** this favors "browse one service over a time range" over "browse
everything, no service filter" (an all-services live-tail default view), since rows are
grouped by `ServiceName` first within each month partition. If that all-services view
proves too slow once the Query API is built against real data, the named follow-up is a
`Timestamp`-first projection (addable non-destructively later) - not solved now.

**Skipping indices** (`bloom_filter` on `TraceId` and the `ResourceAttributes`/
`LogAttributes` map keys+values, `tokenbf_v1` on `Body`) cover the filters that fall
outside the `ORDER BY` prefix: trace/span correlation lookups, arbitrary structured
attribute filtering, and body substring search - all named in Planning.md's dashboard
section.

**Reviewed against the ClickHouse `clickhouse-best-practices` agent skill** (installed
from [`ClickHouse/agent-skills`](https://github.com/ClickHouse/agent-skills)) before
being finalized. Corrections it produced: `SeverityNumber` narrowed `Int32`→`UInt8`
(only ranges 0-24 per `schema-types-minimize-bitwidth`); partitioning changed from daily
to monthly (above). Reviewed and kept as deliberate: `TraceId`/`SpanId` as plain
`String`, not `UUID`/`FixedString` - a reviewed deviation from `schema-types-native-types`,
chosen for interop with the rest of the OTel ecosystem's hex-string convention.

## `LogEvent` field → column mapping

| `LogEvent` field (C#)          | `logs` column        | Notes |
|---------------------------------|-----------------------|-------|
| `Timestamp`                     | `Timestamp`           | See "Timestamp precision" below. |
| `ObservedTimestamp`             | `ObservedTimestamp`   | Nullable in C#, **not** in the DDL - see "Empty string / NULL convention". |
| `SeverityNumber`                | `SeverityNumber`      | `int` (0-24) → `UInt8`. |
| `SeverityText`                  | `SeverityText`        | |
| `Body`                          | `Body`                | |
| `TraceId` / `SpanId`            | `TraceId` / `SpanId`  | Lower-hex, empty string if absent. |
| `TraceFlags`                    | `TraceFlags`          | |
| `ServiceName`                   | `ServiceName`         | Sourced from `service.name` resource attribute. |
| `ResourceSchemaUrl`             | `ResourceSchemaUrl`   | |
| `ResourceAttributes`            | `ResourceAttributes`  | |
| `ScopeSchemaUrl`                | `ScopeSchemaUrl`      | |
| `ScopeName` / `ScopeVersion`    | `ScopeName` / `ScopeVersion` | |
| `ScopeAttributes`               | `ScopeAttributes`     | |
| `LogAttributes`                 | `LogAttributes`       | Renamed from `Attributes` so all three attribute bags mirror their columns 1:1. |
| `EventName`                     | `EventName`           | Presence marks a named OTel "event", not just descriptive text. |

## Empty string / NULL convention

Nullable C# strings (`SeverityText`, `ScopeName`, `ScopeVersion`, `*SchemaUrl`,
`TraceId`, `SpanId`, `EventName`) map to plain (non-`Nullable`) ClickHouse columns -
same convention the OTel Collector exporter uses, and it keeps every string/attribute
column a cheaper non-`Nullable` type (`schema-types-avoid-nullable`). The future
insert pipeline coalesces `null → ""` at insert time.

`ObservedTimestamp` needs a specific call-out: it's nullable in `LogEvent` (only set
when the OTLP record itself set `observed_time_unix_nano`), but the DDL's
`ObservedTimestamp DateTime64(9)` column is **not** nullable. The batched insert
pipeline (a later roadmap item) is responsible for defaulting it to `Timestamp` when
the model value is `null` - this migration surfaces that requirement but doesn't
implement it.

## Timestamp precision

The DDL uses `DateTime64(9)` (nanosecond) to match OTLP's own wire precision and stay
interoperable with other OTLP-ClickHouse tooling. `Flare.Ingest`'s current mapping path
(`OtlpLogMapper.FromUnixNano`) converts via .NET `DateTimeOffset`, which only has 100ns
(tick) resolution - so today's data is stored with trailing digits at nanosecond
granularity, not true nanosecond fidelity. That's an accepted trade-off (.NET simply
can't represent true nanoseconds); 100ns resolution is well beyond what time-range
filtering or event ordering in the dashboard needs.

## Migration convention

Numbered files, applied in order: `NNNN_description.sql`. Never edit a shipped
migration once merged - add a new numbered file instead. There's no automated migration
runner yet beyond the local-dev init-mount below; production migration tooling is a gap
future roadmap items (starting with `docker-compose.yml`) need to close.

## How this gets applied

**Local dev (Aspire):** `Flare.AppHost` mounts this directory into the ClickHouse
container's init-script path, so `0001_logs.sql` runs automatically the first time the
container starts against an empty data directory (see `Program.cs` for exactly which
Aspire API was used, confirmed against real source rather than guessed). Confirmed by
actually running it: the official image's init scripts execute against the `default`
database, not whatever logical database `AddDatabase("clickhousedb")` provisions on the
Aspire side - there's no `CLICKHOUSE_DB` env var connecting the two. That's why
`0001_logs.sql` explicitly does `CREATE DATABASE IF NOT EXISTS clickhousedb` and
qualifies the table as `clickhousedb.logs` itself, rather than assuming the container's
default database is the right target.

**Everywhere else:** no apply tooling exists yet - flagged as a gap for the
`docker-compose.yml` v1 roadmap item to close.

## Verifying the schema

Run these against ClickHouse's HTTP interface once the AppHost is up. Aspire proxies the
container's port 8123 to a **dynamic host port** - don't assume 8123 locally. Get both
the host port and the auto-generated password from the Aspire dashboard's `clickhouse`
resource details (or `mcp__aspire__list_resources`'s `urls`/environment for that
resource) and export them:

```bash
export CH_PORT='<the "http" url's port from the Aspire dashboard, e.g. 49538>'
export CH_PASSWORD='<from the Aspire dashboard>'
export CH="http://localhost:$CH_PORT/?database=clickhousedb&user=default&password=$CH_PASSWORD"
```

(Equivalently: `docker exec <clickhouse container> clickhouse-client --database=clickhousedb -q "..."` works too, and skips the port/password lookup entirely - useful for a quick check.)

**1. Confirm the init-mount applied the schema:**

```bash
curl -s "$CH" --data-binary "SELECT count() FROM logs"
# Expect: 0
```

**2. Insert representative rows** - covering no trace/span id, array/kvlist-derived
attribute strings, unicode body, and unspecified severity:

```bash
curl -s "${CH}&query=INSERT+INTO+logs+FORMAT+JSONEachRow" --data-binary @- <<'EOF'
{"Timestamp":"2026-08-07 12:00:00.000000000","ObservedTimestamp":"2026-08-07 12:00:00.000000000","TraceId":"0102030405060708090a0b0c0d0e0f10","SpanId":"0102030405060708","TraceFlags":1,"SeverityText":"Information","SeverityNumber":9,"ServiceName":"flare-ingest","Body":"hello from curl","ResourceSchemaUrl":"","ResourceAttributes":{"service.name":"flare-ingest"},"ScopeSchemaUrl":"","ScopeName":"manual-test","ScopeVersion":"","ScopeAttributes":{},"LogAttributes":{"http.method":"GET"},"EventName":""}
{"Timestamp":"2026-08-07 12:00:01.000000000","ObservedTimestamp":"2026-08-07 12:00:01.000000000","TraceId":"","SpanId":"","TraceFlags":0,"SeverityText":"","SeverityNumber":0,"ServiceName":"flare-ingest","Body":"no trace context, unspecified severity","ResourceSchemaUrl":"","ResourceAttributes":{},"ScopeSchemaUrl":"","ScopeName":"","ScopeVersion":"","ScopeAttributes":{},"LogAttributes":{},"EventName":""}
{"Timestamp":"2026-08-07 12:00:02.000000000","ObservedTimestamp":"2026-08-07 12:00:02.000000000","TraceId":"","SpanId":"","TraceFlags":0,"SeverityText":"Error","SeverityNumber":17,"ServiceName":"payments-api","Body":"boom 💥 支払いに失敗しました","ResourceSchemaUrl":"","ResourceAttributes":{},"ScopeSchemaUrl":"","ScopeName":"","ScopeVersion":"","ScopeAttributes":{},"LogAttributes":{"tags":"[1,2]","context":"{nested=v}"},"EventName":"payment.failed"}
EOF
```

**3. Prove the dashboard's stated query patterns are servable:**

```bash
# Service + time-range filter (uses the ORDER BY prefix directly)
curl -s "$CH" --data-binary \
  "SELECT Body FROM logs WHERE ServiceName = 'flare-ingest' AND Timestamp >= '2026-08-07 00:00:00' ORDER BY Timestamp"

# Arbitrary structured-attribute filter (exercises the LogAttributes skip index)
curl -s "$CH" --data-binary \
  "SELECT Body FROM logs WHERE LogAttributes['http.method'] = 'GET'"

# Volume-by-service-and-level (the dashboard's "simple charts")
curl -s "$CH" --data-binary \
  "SELECT ServiceName, SeverityText, count() FROM logs GROUP BY ServiceName, SeverityText ORDER BY count() DESC"

# Trace id point lookup (exercises the TraceId bloom filter index)
curl -s "$CH" --data-binary \
  "SELECT Body FROM logs WHERE TraceId = '0102030405060708090a0b0c0d0e0f10'"

# Unicode round-trip + array/kvlist-derived attribute strings
curl -s "$CH" --data-binary \
  "SELECT Body, LogAttributes FROM logs WHERE ServiceName = 'payments-api'"
```

Every query above should return the matching row(s) with the data intact (including the
emoji/Japanese text and the `[1,2]`/`{nested=v}` attribute strings) - that's the real
proof the schema works against representative data, not just that the DDL parses.
Re-run this section by hand whenever a new numbered migration changes the schema.
