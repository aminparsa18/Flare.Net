# ADR-0008: OTel attributes flatten to `Map(LowCardinality(String), String)`; trace/span ids stay plain `String`

Status: Accepted
Date: 2026-08-07 (v1, `0001_logs.sql`) — resolves Planning.md's open
question #2 ("Map vs. JSON vs. dynamic columns")

## Context

Every OTLP `AnyValue` attribute variant (string/bool/int/double/bytes/
array/kvlist) needs a ClickHouse column shape, across three attribute bags
per signal (resource, instrumentation scope, log/span/data-point). Query
performance was explicitly flagged up front as depending on getting this
right (Flare's own pre-v1 "Open questions" list). Separately, trace and
span identifiers needed a column type — ClickHouse has native `UUID`/
`FixedString` types that look like an obvious fit for a fixed-length hex
identifier.

## Decision

**Attributes**: flatten every `AnyValue` variant to `string` at ingest
(`OtlpLogMapper` and its trace/metric equivalents), landing in one of
three `Map(LowCardinality(String), String)` columns per table —
`ResourceAttributes`, `ScopeAttributes`, and a signal-specific bag
(`LogAttributes`/`SpanAttributes`/`DataPointAttributes`) — mirroring
OTel's own three attribute-bag structure.

**Trace/span ids**: `TraceId`/`SpanId`/`ParentSpanId` stay plain `String`
(lower-hex), not `UUID`/`FixedString`.

## Alternatives considered

- **JSON column type** or **dynamic columns** for attributes — the two
  alternatives the original open question named explicitly. Not chosen for
  v1; `Map(LowCardinality(String), String)` was picked instead as the
  proven, unreinvented approach: it's the same shape the OpenTelemetry
  Collector's own ClickHouse exporter and SigNoz both use.
- **`UUID`/`FixedString` for `TraceId`/`SpanId`** — the `clickhouse-best-practices`
  skill's `schema-types-native-types` rule recommends exactly this for
  fixed-length identifiers, and the schema was explicitly reviewed against
  that skill before being finalized. Kept as plain `String` anyway, as a
  reviewed, deliberate deviation: OTel's whole ecosystem treats trace/span
  ids as hex strings on the wire and in every other tool's UI/API/logs
  interop story, and matching that convention was judged more valuable
  than the native type's storage/comparison efficiency.

## Consequences

- Attribute filtering and structured-attribute queries go through skip
  indices on the `Map` columns' keys and values (`bloom_filter`), not a
  native JSON path-query engine — this is a real, accepted trade-off in
  query expressiveness for the proven-not-reinvented posture above.
- Because the attribute shape mirrors the OTel Collector's own exporter
  and SigNoz, tooling and query patterns familiar from that ecosystem
  transfer directly — this was a stated benefit of the choice, not
  incidental.
- Trace/span ids being plain strings means they interop cleanly with every
  other OTel-ecosystem tool's hex-string convention, at the cost of the
  native-type storage/comparison efficiency `schema-types-native-types`
  would otherwise recommend.
- This decision was reviewed once, against a specific version of the
  `clickhouse-best-practices` skill, and is not automatically re-validated
  against future revisions of that skill's rules.

## Related documentation

- `db/clickhouse/README.md` — the full field→column mapping tables this
  decision underlies
- `docs-internal/investigations/benchmark-ingest-and-query.md` — query
  latency measurements against this schema shape