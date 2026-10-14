# ADR-0217: A Tempo-compatible trace query API subset

Status: Accepted

Date: 2026-10-14

## Context

Grafana reads traces through its Tempo data source, which speaks the Tempo HTTP API. Flare stores OTel
spans in ClickHouse and has its own trace endpoints (`GET /api/traces/{id}`, `POST /api/spans/search`)
with Flare's own shapes, so a Grafana that already has dashboards, Explore and trace-to-log links built
on Tempo cannot read Flare traces. ADR-0109 gave metrics the same treatment with a Prometheus subset;
the Grafana interoperability work lists traces next.

Tempo's search is TraceQL, a query language with its own evaluation model (spansets, pipelines,
aggregates, structural operators). Reimplementing all of it would be a project of its own, and an
incomplete TraceQL that returns wrong traces is worse than a clear error.

## Decision

- **A read-only subset of the Tempo HTTP API under `/tempo/api`,** on the normal authenticated route
  group: `echo` (Grafana's connection test), `traces/{id}` and `v2/traces/{id}`, `search`,
  `search/tags` and `v2/search/tags`, `search/tag/{tag}/values` and `v2/search/tag/{tag}/values`. Auth is
  the existing pipeline, so a personal access token (ADR-0019) as `Authorization: Bearer flr_pat_...` is
  all a client needs, and PAT rate limiting (ADR-0028) applies. A Grafana data source URL is
  `http://flare:8080/tempo`.
- **Under a `/tempo` prefix, not at `/api`.** Flare already serves `GET /api/traces/{id}` with its own
  response, and Tempo's route of the same name cannot share it. The prefix is the data source URL, which
  Grafana supports. The Prometheus API needed no prefix because `/api/v1` was free.
- **Trace by id returns OTLP, written by hand.** `tempopb.Trace` is wire-compatible with OTLP
  `TracesData`, and Grafana's data source requests `application/protobuf`; any other `Accept` gets
  OTLP-style JSON (`{"batches":[...]}` for v1, `{"trace":{"resourceSpans":[...]}}` for v2). The v2 body
  also carries Tempo's partial status. `TempoTraceEncoder` writes the protobuf wire format directly
  (varints, fixed64, length-delimited), because the OTLP `.proto` files are vendored into `Flare.Ingest`
  and the API project deliberately does not share types across the service boundary. A test decodes its
  output with the generated OTLP types, so the encoder is checked against the real schema. Two details come
  from Grafana 13.2 itself, whose Tempo plugin panics without them: every span carries a `status` even when
  unset, and the v2 response always carries the `metrics` submessage. Spans are
  grouped into `ResourceSpans` and `ScopeSpans` the way OTLP nests them. A trace Flare returns only the
  top levels of (`TraceDto.Truncated`) is reported as `PARTIAL` in v2.
- **Search is `RootSpansOnly` plus a structural filter.** Tempo returns traces; Flare's span search
  returns spans. A search is one row per trace (the root span carries `rootServiceName`, `rootTraceName`,
  start and duration) narrowed by `SpanFilter.Structure`, the trace-level "contains a span like this"
  evaluation behind the Traces page's structure builder (ADR-0069). Each TraceQL spanset is one
  structure condition, and the spanset operators map onto its expression: `&&`, `||`, `>` (`->`, direct
  child) and `>>` (`=>`, descendant). No new SQL builder.
- **A TraceQL subset with a hand-written parser,** `TraceQlParser`: spansets of `field op value`
  predicates joined by `&&`; fields `name`, `status`, `duration`, `span.*`, `resource.*`, unscoped `.*`,
  and the trace-level `rootName`, `rootServiceName`, `traceDuration`; operators `= != =~ !~ > >= < <=`;
  string, number, duration, boolean and status values. Everything else (`|` pipelines such as
  `count()`, `select()` and `by()`, `||` inside a spanset, `kind`, `statusMessage`, `name` with anything
  but `=`, negated and sibling structural operators, field-to-field comparison) is a 400 that names the
  construct. `TempoSearchPlanner` compiles the parse into conditions; the parser and planner are pure and
  unit-tested without ClickHouse.
- **The legacy `tags` parameter** (logfmt `key=value` pairs, all on one span) and `minDuration` /
  `maxDuration` go through the same planner. `q` and `tags` together are an error, as in Tempo.
- **Trace-level intrinsics filter the root row,** so they are accepted only when the query is a single
  spanset. In `{ rootName = "a" } || { ... }` they would silently mean something else.
- **Tempo anchors regular expressions; ClickHouse `match()` does not.** The planner wraps every pattern in
  `^(?:...)$`.
- **Unscoped attributes.** Tempo's `.foo` means "span or resource"; Flare's attribute filters name one
  bag. An unscoped key is read from the resource bag when it is in an OpenTelemetry resource namespace
  (`service.`, `k8s.`, `host.`, `cloud.`, `deployment.`, `process.`, ...) and from the span bag otherwise.
  Writing `span.x` or `resource.x` removes the guess. Matching both bags would need an `OR` per predicate,
  which grows combinatorially inside one spanset.
- **Tag discovery samples.** `search/tags` lists the most common attribute keys of each bag among at most
  50,000 spans of the window (`SpanAttributeKeysQueryBuilder`): `arrayJoin(mapKeys(...))` multiplies rows
  by map size, so it must not run over a whole window. Tag values reuse the existing
  attribute-values query. Both default to the last hour, like the other search endpoints.
- **`TraceSpanCondition.MaxDurationNano`,** appended last (MemoryPack-compatible), so TraceQL
  `duration < x` has an upper bound to compile to. A strict bound is one nanosecond inside the
  inclusive one.
- **Errors are plain text with a status code,** as Tempo writes them.

Verified end to end against Grafana 13.2.4 and a real ClickHouse: the data source's connection test, a
TraceQL search (rendered as Grafana's traces table) and a trace-by-id lookup (rendered as a trace frame).

## Alternatives considered

- **Port or embed Tempo's TraceQL engine.** Rejected: it assumes Tempo's storage and block format, and a
  port would carry its whole evaluation surface to maintain.
- **Generate dedicated ClickHouse SQL per TraceQL construct.** Rejected: the structural filter already
  evaluates trace-level span matching, direct and indirect parentage and boolean combination, with its
  own tests. A second implementation would drift.
- **Serve Tempo's routes at `/api` and move Flare's trace-by-id.** Rejected: it breaks the dashboard, the
  CLI and MCP wire contract for no gain over a prefix.
- **Use the generated OTLP types by referencing `Flare.Ingest`.** Rejected: the API project does not
  reference the write side. The encoder is about 150 lines and is tested against the generated types.
- **`spanSets` in search results, and the metrics-generator endpoints** (`/api/metrics/query_range`,
  TraceQL metrics). Deferred, not rejected; see the roadmap.

## Consequences

- Grafana's Tempo data source can look up a trace, run a TraceQL or tag search, and fill its query
  builder's tag and value pickers against Flare.
- A trace whose root span was never ingested does not appear in search (it has no root row), but is
  found by id.
- Search results carry no `spanSets`: Grafana's TraceQL results table lists traces, not the matching spans
  inside them.
- `status != error`, `name =~ ...` and `kind` are rejected. They need new columns in the structure
  condition rather than new parsing.
- An unscoped attribute outside the known resource namespaces is read from the span bag; a custom
  resource attribute needs `resource.` in front.
- TraceQL's `!=` means "has the attribute and it differs"; Flare's means "absent or different".
- Nested spans past 10,000 per trace are cut by the existing trace-by-id cap, reported as `PARTIAL` in v2
  only.
