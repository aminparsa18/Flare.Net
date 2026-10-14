# ADR-0221: TraceQL metrics (`rate()`, `quantile_over_time()`, ...) over spans

Status: Accepted

Date: 2026-10-14

Extends [ADR-0217](0217-tempo-compatible-trace-api.md), which rejected every `|` pipeline.

## Context

Grafana's Tempo data source draws time series from a TraceQL query that ends in a metrics function
(`{ status = error } | rate() by (resource.service.name)`), calling `GET /api/metrics/query_range` (and
`/api/metrics/query` for an instant value). It sends `q`, `start`, `end` and `step` (unix seconds and a
duration such as `30s`) and `Accept: application/json`. Tempo's engine computes these over spans as they are
read; Flare's spans are in ClickHouse, which can aggregate them directly. The whole TraceQL pipeline
language (`select()`, `count() > 3`, `compare()`, second-stage `topk()`) is far larger than what a
dashboard panel needs, so this takes the part a panel uses.

## Decision

- **Syntax: one spanset, then one metrics stage.** `{ ... } | function(...) [by (field, ...)]`. The spanset is
  anything the search subset accepts for one span (intrinsics `name`, `status`, `kind`, `duration`; `span.`,
  `resource.` and `.` attributes; every operator), compiled by `TempoSearchPlanner` into a
  `TraceSpanCondition` and matched per span by `TraceStructureSqlBuilder.ConditionMatch`, so a metrics query and
  a search cannot disagree about which spans match. Not accepted: several spansets or structural operators
  (a metric over "spans that have a descendant" has no clear per-span meaning), trace-level intrinsics
  (`rootName`, `traceDuration`), a second pipeline stage, and hints other than `with(exemplars=...)`,
  which is accepted and ignored because Flare returns no exemplars.
- **Functions.** `rate()` (spans per second), `count_over_time()` (spans per step), `quantile_over_time(duration,
  q, ...)` (one to ten quantiles in `[0, 1]`), and `avg_over_time`, `min_over_time`, `max_over_time`,
  `sum_over_time` of `duration`. Durations are reported in seconds, as Tempo does. Not accepted:
  `histogram_over_time()` and `compare()`, and a function over an attribute rather than `duration` (each
  needs a numeric cast of a string map value and a decision about spans where it does not parse; both can be
  added without changing what is here).
- **`by (...)`: at most five of `name`, `status`, `kind`, `span.x`, `resource.x`, `.x`.** A span without a grouped
  attribute is left out of the result rather than grouped under an empty value, as in Tempo. An attribute with
  old and new semantic-convention spellings groups by whichever the span has (`SemconvAliases`). Labels are
  named as written in the query (`span.http.route`, `resource.service.name`, `name`) with a string value;
  `status` and `kind` use TraceQL's words (`error`, `server`).
- **One ClickHouse query.** `TraceQlMetricsQueryBuilder` groups matching spans by
  `intDiv(toUnixTimestamp(StartTime) - start, step)` and the `by` fields, and aggregates in the database
  (`sum`, `min`, `max`, `quantileTDigestWeighted`). `start` is rounded down and `end` up to a multiple of `step`,
  and a sample is stamped with its bucket's start, as in Flare's Prometheus API (ADR-0109).
  `rate()` is the summed weight over the step's seconds.
- **Sampled spans count for what they stand for.** Counts, sums and quantiles weight each row by
  `SampleWeight` (ADR-0122), so a service sampled 1 in 10 still reports its true rate; `min` and `max` are
  unweighted. Without sampling every weight is 1.
- **Series are capped at 200,** the largest by span count. With a `by` clause the query reads the spans twice:
  once to find the top groups, once to aggregate them. A truncated result carries `status: PARTIAL` and a
  `message`, as in Tempo's response, never a silent cut.
- **Gaps.** `rate()` and `count_over_time()` are zero in a step with no spans, so every series covers the
  whole range; the others omit steps with no spans, since "no spans" has no average.
- **Time parameters.** `start` and `end` are unix seconds, unix nanoseconds or RFC3339 (as Tempo documents);
  `since` replaces both (default one hour). `step` is a duration or whole seconds, at least one second; absent,
  it is the range divided into 200 steps, rounded up to a second. At most 1,500 steps per query, a 400 above that.
  `exemplars` is accepted and ignored.
- **Response (JSON, Tempo's field names).** Range: `{"series":[{"labels":[{"key":...,"value":{"stringValue":...}}],
  "samples":[{"timestampMs":"...","value":...}]}],"metrics":{...},"status":...}`. Instant
  (`/api/metrics/query`): `series[].value`, the function over the whole range. Quantile series carry a label
  `p` with the quantile as a `doubleValue`; a query without `by` carries `__name__` (the function's name) so the
  series has a label.
- **Under `/tempo/api/metrics`,** like the rest (ADR-0217): data source URL `http://flare:8080/tempo`.
  Normal authentication and project scope apply; the caps every query carries apply.

## Alternatives considered

- **Evaluate in memory over fetched spans, as the Prometheus API does for metric series.** Rejected: spans
  are orders of magnitude more numerous than metric points, and ClickHouse aggregates them where they sit.
- **Read the span-derived RED rollups (ADR-0030) instead of raw spans.** Fast, but they only group by service,
  operation and status, not by arbitrary attributes or an arbitrary spanset.
- **Support the structural spansets.** Deferred: it is a per-trace evaluation that a per-step aggregate
  cannot express without a second pass per step.

## Consequences

- A Grafana TraceQL panel with `rate()`, `quantile_over_time()` or `count_over_time()` works against Flare,
  including the Explore "Metrics" query type, as does the instant (single value) query type.
- Cost is one scan of the matching spans in the window (two with `by`), bounded by the query caps; a wide
  range with a loose spanset on a busy installation is a slow query, as it is in the Traces page.
- Trace-level intrinsics, structural spansets, `histogram_over_time` and attribute-valued functions stay
  unsupported and are rejected naming the construct.
